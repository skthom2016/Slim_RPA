using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracto.TerminalRuntime.Application.Commands;
using Tracto.TerminalRuntime.Domain.Commands;
using Tracto.TerminalRuntime.Domain.Shells;

namespace Tracto.TerminalRuntime.Infrastructure.Commands;

// ── Policy document shapes (file-scoped: not part of the public API) ─────────

internal sealed record PolicyDocument(
    string Signature,
    PolicyEntry[] Entries);

internal sealed record PolicyEntry(
    string CommandRef,
    string ExecutablePath,
    string ExpectedSha256,
    bool IsApproved,
    string[] SupportedShells);

// ── Registry ──────────────────────────────────────────────────────────────────

/// <summary>
/// Loads the approved command list from a signed deployment policy JSON file
/// and exposes it as <see cref="ICommandRegistry"/>.
///
/// <para>
/// The policy document carries an HMAC-SHA256 signature that covers a canonical
/// representation of all entries (sorted by <c>CommandRef</c> for determinism).
/// When the key is configured and the signature is invalid the registry starts
/// empty, so no command can pass allowlist checks.
/// </para>
///
/// <para>
/// The signing key is never written to logs, evidence or telemetry payloads
/// (CEC v1.0.1 invariant #4).
/// </para>
/// </summary>
public sealed class CommandRegistry : ICommandRegistry
{
    private readonly IReadOnlyDictionary<string, CommandRegistryEntry> _entries;
    private readonly ILogger<CommandRegistry> _logger;

    public CommandRegistry(
        IOptions<CommandRegistrySettings> settings,
        ILogger<CommandRegistry> logger)
    {
        _logger = logger;
        _entries = LoadAndVerify(settings.Value);
    }

    /// <inheritdoc/>
    public CommandRegistryEntry? TryGet(string commandRef) =>
        _entries.TryGetValue(commandRef, out var entry) ? entry : null;

    // ── private helpers ───────────────────────────────────────────────────────

    private IReadOnlyDictionary<string, CommandRegistryEntry> LoadAndVerify(
        CommandRegistrySettings cfg)
    {
        if (string.IsNullOrWhiteSpace(cfg.PolicyFilePath))
        {
            _logger.LogWarning(
                "CommandRegistry:PolicyFilePath is not configured. Registry will be empty.");
            return new Dictionary<string, CommandRegistryEntry>(StringComparer.OrdinalIgnoreCase);
        }

        if (!File.Exists(cfg.PolicyFilePath))
        {
            _logger.LogWarning(
                "Command registry policy file was not found at the configured path. " +
                "Registry will be empty.");
            return new Dictionary<string, CommandRegistryEntry>(StringComparer.OrdinalIgnoreCase);
        }

        PolicyDocument? doc;
        try
        {
            var json = File.ReadAllText(cfg.PolicyFilePath, Encoding.UTF8);
            doc = JsonSerializer.Deserialize<PolicyDocument>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to parse command registry policy file. Registry will be empty.");
            return new Dictionary<string, CommandRegistryEntry>(StringComparer.OrdinalIgnoreCase);
        }

        if (doc is null)
        {
            _logger.LogError(
                "Command registry policy file is empty or malformed. Registry will be empty.");
            return new Dictionary<string, CommandRegistryEntry>(StringComparer.OrdinalIgnoreCase);
        }

        // Verify the document signature when a key is configured.
        if (!string.IsNullOrWhiteSpace(cfg.HmacKeyBase64))
        {
            byte[] keyBytes;
            try
            {
                keyBytes = Convert.FromBase64String(cfg.HmacKeyBase64);
            }
            catch
            {
                _logger.LogError(
                    "CommandRegistry:HmacKeyBase64 is not valid Base-64. Registry will be empty.");
                return new Dictionary<string, CommandRegistryEntry>(StringComparer.OrdinalIgnoreCase);
            }

            if (!VerifyDocumentSignature(doc, keyBytes))
            {
                _logger.LogError(
                    "Command registry policy document signature verification failed. " +
                    "Registry will be empty to prevent execution of untrusted commands.");
                return new Dictionary<string, CommandRegistryEntry>(StringComparer.OrdinalIgnoreCase);
            }
        }
        else if (cfg.AllowUnsignedPolicy)
        {
            // Operator has explicitly opted in to skipping signature verification.
            // This must never be enabled in production.
            _logger.LogWarning(
                "CommandRegistry:HmacKeyBase64 is not configured and AllowUnsignedPolicy is true. " +
                "Policy document signature will NOT be verified — " +
                "this mode is for development only and must not be used in production.");
        }
        else
        {
            // Key absent and opt-in flag not set: fail closed to prevent an
            // accidental misconfiguration from silently bypassing document-signature
            // verification and populating the allowlist with unverified entries.
            _logger.LogError(
                "CommandRegistry:HmacKeyBase64 is not configured and AllowUnsignedPolicy is false. " +
                "Registry will be empty. Set AllowUnsignedPolicy=true explicitly to permit " +
                "unsigned policies in development environments.");
            return new Dictionary<string, CommandRegistryEntry>(StringComparer.OrdinalIgnoreCase);
        }

        return BuildEntryMap(doc);
    }

    private IReadOnlyDictionary<string, CommandRegistryEntry> BuildEntryMap(PolicyDocument doc)
    {
        var result = new Dictionary<string, CommandRegistryEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var e in doc.Entries)
        {
            ShellType[] shells;
            try
            {
                shells = e.SupportedShells
                    .Select(s => Enum.Parse<ShellType>(s, ignoreCase: true))
                    .ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Skipping registry entry '{CommandRef}': unrecognised shell type.",
                    e.CommandRef);
                continue;
            }

            result[e.CommandRef] = new CommandRegistryEntry(
                e.CommandRef,
                e.ExecutablePath,
                e.ExpectedSha256,
                e.IsApproved,
                shells);
        }

        _logger.LogInformation(
            "Command allowlist registry loaded with {Count} entries.", result.Count);
        return result;
    }

    /// <summary>
    /// Verifies the HMAC-SHA256 signature over the canonical policy payload.
    /// Constant-time comparison prevents timing attacks.
    /// </summary>
    private static bool VerifyDocumentSignature(PolicyDocument doc, byte[] keyBytes)
    {
        var canonical = BuildCanonicalPayload(doc);
        var payloadBytes = Encoding.UTF8.GetBytes(canonical);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(payloadBytes);
        var expectedHex = Convert.ToHexString(hash).ToLowerInvariant();

        // Pad/align the two strings to the same length for FixedTimeEquals.
        var actualHexBytes = Encoding.UTF8.GetBytes(doc.Signature.ToLowerInvariant());
        var expectedHexBytes = Encoding.UTF8.GetBytes(expectedHex);

        if (actualHexBytes.Length != expectedHexBytes.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(expectedHexBytes, actualHexBytes);
    }

    /// <summary>
    /// Builds a deterministic canonical string over all policy entries
    /// (sorted by CommandRef) used for signature verification and signing.
    /// Internal visibility for test access.
    /// </summary>
    internal static string BuildCanonicalPayload(PolicyDocument doc)
    {
        var sorted = doc.Entries
            .OrderBy(e => e.CommandRef, StringComparer.Ordinal)
            .Select(e =>
            {
                var shells = string.Join(",",
                    e.SupportedShells.OrderBy(s => s, StringComparer.Ordinal));
                return $"{e.CommandRef}|{e.ExecutablePath}|{e.ExpectedSha256}|{e.IsApproved}|{shells}";
            });

        return string.Join(";", sorted);
    }
}
