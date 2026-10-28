using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracto.TerminalRuntime.Application.Commands;
using Tracto.TerminalRuntime.Application.Interfaces;
using Tracto.TerminalRuntime.Domain.Policies;

namespace Tracto.TerminalRuntime.Infrastructure.Commands;

/// <summary>
/// Computes the SHA-256 digest of a file on disk and compares it against
/// the expected hex string from a <c>CommandRegistryEntry</c>.
///
/// <para>
/// All file I/O is isolated here so the Application layer stays clean of
/// filesystem concerns (TER-US-006, CEC v1.0.1 invariant #5).
/// </para>
///
/// <para>
/// Before opening any file, the supplied path is validated against the configured
/// <c>ApprovedExecutableRoots</c> via <see cref="IPathValidator"/> (CEC invariant: all
/// file paths must be checked against approved roots before any I/O).
/// </para>
/// </summary>
public sealed class Sha256Verifier : ICommandHashVerifier
{
    private readonly IPathValidator _pathValidator;
    private readonly IOptions<CommandRegistrySettings> _settings;
    private readonly ILogger<Sha256Verifier> _logger;

    public Sha256Verifier(
        IPathValidator pathValidator,
        IOptions<CommandRegistrySettings> settings,
        ILogger<Sha256Verifier> logger)
    {
        _pathValidator = pathValidator;
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns <c>false</c> without throwing when the file does not exist, falls outside
    /// the approved executable roots, or cannot be read — callers receive a clean rejection
    /// rather than an unhandled exception.
    /// </remarks>
    public bool VerifyFileHash(string filePath, string expectedHex)
    {
        // Phase 0 — validate the path against approved executable roots before any I/O.
        // An empty ApprovedExecutableRoots list (the default) denies all paths (fail-safe).
        var policy = new WorkingDirPolicy(_settings.Value.ApprovedExecutableRoots);
        var validation = _pathValidator.Validate(filePath, policy);
        if (validation.IsFailure)
            return false;

        if (!File.Exists(filePath))
            return false;

        string actualHex;
        try
        {
            using var stream = File.OpenRead(filePath);
            var hashBytes = SHA256.HashData(stream);
            actualHex = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not read file for hash verification: {FilePath}", filePath);
            return false;
        }

        return string.Equals(
            actualHex,
            expectedHex.ToLowerInvariant(),
            StringComparison.Ordinal);
    }
}
