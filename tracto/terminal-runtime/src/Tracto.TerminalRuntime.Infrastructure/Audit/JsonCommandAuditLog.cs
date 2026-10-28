using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Tracto.TerminalRuntime.Application.Audit;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Audit;

namespace Tracto.TerminalRuntime.Infrastructure.Audit;

/// <summary>
/// <see cref="ICommandAuditLog"/> implementation that writes one JSON audit file
/// per run at <c>%LOCALAPPDATA%\Tracto\TerminalRuntime\audit\{runId}.jsonl</c>.
///
/// CEC v1.0.1 invariant #4 is enforced by design:
/// <list type="bullet">
///   <item>Only argument NAMES are stored; raw values are never written.</item>
///   <item>Only SHA-256 hashes of artifacts are stored; content is never written.</item>
///   <item><c>IdentityRef</c> is an opaque reference, not a plaintext credential.</item>
/// </list>
///
/// All paths are validated against the approved audit root to prevent directory traversal.
/// Append operations are idempotent for identical (RunId, AttemptId) pairs.
/// </summary>
public sealed class JsonCommandAuditLog : ICommandAuditLog
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented               = false,
        PropertyNameCaseInsensitive = true,
        Converters                  = { new JsonStringEnumConverter() }
    };

    private readonly string _auditRoot;
    private readonly ILogger<JsonCommandAuditLog> _logger;

    public JsonCommandAuditLog(
        ILogger<JsonCommandAuditLog> logger,
        string? auditRootOverride = null)
    {
        _logger    = logger;
        _auditRoot = auditRootOverride
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tracto", "TerminalRuntime", "audit");
        Directory.CreateDirectory(_auditRoot);
    }

    // ── AppendAsync ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<Result<bool, Error>> AppendAsync(
        CommandAuditRecord record,
        CancellationToken ct = default)
    {
        try
        {
            var filePath = ResolveAndValidatePath(record.RunId);

            // Idempotency: skip if this (RunId, AttemptId) is already in the file.
            if (File.Exists(filePath))
            {
                var existing = await ReadLinesAsync(filePath, ct).ConfigureAwait(false);
                foreach (var line in existing)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var parsed = JsonSerializer.Deserialize<CommandAuditRecord>(line, SerializerOptions);
                        if (parsed is not null
                            && string.Equals(parsed.RunId,      record.RunId,      StringComparison.Ordinal)
                            && string.Equals(parsed.AttemptId,  record.AttemptId,  StringComparison.Ordinal))
                        {
                            _logger.LogDebug(
                                "Audit record for run {RunId} attempt {AttemptId} already exists — skipping duplicate append.",
                                record.RunId, record.AttemptId);
                            return Result.Ok<bool>(true);
                        }
                    }
                    catch (JsonException)
                    {
                        // corrupt line — ignore during idempotency check
                    }
                }
            }

            // Append a single JSON line (JSONL / newline-delimited JSON).
            var json = JsonSerializer.Serialize(record, SerializerOptions);
            await File.AppendAllTextAsync(filePath, json + Environment.NewLine, ct).ConfigureAwait(false);

            _logger.LogDebug(
                "Audit record appended for run {RunId} attempt {AttemptId} command {CommandRef}.",
                record.RunId, record.AttemptId, record.CommandRef);

            return Result.Ok<bool>(true);
        }
        catch (UnauthorizedAccessException uae)
        {
            _logger.LogError("Audit path validation failed: {Reason}", uae.Message);
            return Result.Fail<bool>(Error.General(
                "Audit.PathViolation",
                "Audit path is outside the approved root."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to append audit record for run {RunId}.", record.RunId);
            return Result.Fail<bool>(Error.General(
                "Audit.WriteFailure",
                "Failed to persist command audit record."));
        }
    }

    // ── GetByRunIdAsync ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<CommandAuditRecord>, Error>> GetByRunIdAsync(
        string runId,
        CancellationToken ct = default)
    {
        try
        {
            var filePath = ResolveAndValidatePath(runId);
            if (!File.Exists(filePath))
                return Result.Ok<IReadOnlyList<CommandAuditRecord>>(Array.Empty<CommandAuditRecord>());

            var lines   = await ReadLinesAsync(filePath, ct).ConfigureAwait(false);
            var records = new List<CommandAuditRecord>();
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var record = JsonSerializer.Deserialize<CommandAuditRecord>(line, SerializerOptions);
                    if (record is not null)
                        records.Add(record);
                }
                catch (JsonException jex)
                {
                    _logger.LogWarning(jex,
                        "Skipping malformed audit line in file for run {RunId}.", runId);
                }
            }
            return Result.Ok<IReadOnlyList<CommandAuditRecord>>(records.AsReadOnly());
        }
        catch (UnauthorizedAccessException uae)
        {
            _logger.LogError("Audit path validation failed: {Reason}", uae.Message);
            return Result.Fail<IReadOnlyList<CommandAuditRecord>>(Error.General(
                "Audit.PathViolation",
                "Audit path is outside the approved root."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read audit records for run {RunId}.", runId);
            return Result.Fail<IReadOnlyList<CommandAuditRecord>>(Error.General(
                "Audit.ReadFailure",
                "Failed to read command audit records."));
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string ResolveAndValidatePath(string runId)
    {
        var safeName = Path.GetFileName(runId);
        if (string.IsNullOrWhiteSpace(safeName))
            throw new UnauthorizedAccessException("RunId produced an empty file name.");
        var fullPath = Path.GetFullPath(Path.Combine(_auditRoot, $"{safeName}.jsonl"));
        if (!fullPath.StartsWith(_auditRoot, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                "Resolved audit path is outside the approved audit root.");
        return fullPath;
    }

    private static async Task<string[]> ReadLinesAsync(string filePath, CancellationToken ct)
    {
        var text = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }
}
