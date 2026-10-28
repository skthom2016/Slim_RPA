using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Tracto.TerminalRuntime.Application;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain;
using Tracto.TerminalRuntime.Domain.Wsl;

namespace Tracto.TerminalRuntime.Infrastructure;

/// <summary>
/// <see cref="IRunJournal"/> implementation that persists each run as a single
/// JSON file at <c>%LOCALAPPDATA%\Tracto\TerminalRuntime\journal\{runId}.json</c>.
/// No plaintext secrets are written to any persisted field (CEC invariant #4).
/// Every path is validated against the journal root to prevent directory traversal.
/// </summary>
public sealed class JsonRunJournal : IRunJournal
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented               = true,
        PropertyNameCaseInsensitive = true,
        Converters                  = { new JsonStringEnumConverter() }
    };

    private readonly string _journalRoot;
    private readonly ILogger<JsonRunJournal> _logger;

    public JsonRunJournal(ILogger<JsonRunJournal> logger, string? journalRootOverride = null)
    {
        _logger      = logger;
        _journalRoot = journalRootOverride
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tracto", "TerminalRuntime", "journal");
        Directory.CreateDirectory(_journalRoot);
    }

    // ── WriteEntryAsync ───────────────────────────────────────────────────────

    public async Task<Result<bool, Error>> WriteEntryAsync(
        RunJournalEntry entry,
        CancellationToken ct = default)
    {
        try
        {
            var filePath = ResolveAndValidatePath(entry.RunId);
            var json     = JsonSerializer.Serialize(entry, SerializerOptions);
            await File.WriteAllTextAsync(filePath, json, ct).ConfigureAwait(false);
            _logger.LogDebug("Journal entry written for run {RunId} attempt {AttemptId} state {State}.",
                entry.RunId, entry.AttemptId, entry.State);
            return Result.Ok<bool>(true);
        }
        catch (UnauthorizedAccessException uae)
        {
            _logger.LogError("Journal path validation failed: {Reason}", uae.Message);
            return Result.Fail<bool>(Error.General("Journal.PathViolation", "Journal path is outside the approved root."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write journal entry for run {RunId}.", entry.RunId);
            return Result.Fail<bool>(Error.General("Journal.WriteFailure", "Failed to persist journal entry."));
        }
    }

    // ── ReadEntryAsync ────────────────────────────────────────────────────────

    public async Task<Result<RunJournalEntry?, Error>> ReadEntryAsync(
        string runId,
        CancellationToken ct = default)
    {
        try
        {
            var filePath = ResolveAndValidatePath(runId);
            if (!File.Exists(filePath))
                return Result<RunJournalEntry?, Error>.Success(null);
            var json  = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
            var entry = JsonSerializer.Deserialize<RunJournalEntry>(json, SerializerOptions);
            return Result<RunJournalEntry?, Error>.Success(entry);
        }
        catch (UnauthorizedAccessException uae)
        {
            _logger.LogError("Journal path validation failed: {Reason}", uae.Message);
            return Result.Fail<RunJournalEntry?>(Error.General("Journal.PathViolation", "Journal path is outside the approved root."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read journal entry for run {RunId}.", runId);
            return Result.Fail<RunJournalEntry?>(Error.General("Journal.ReadFailure", "Failed to read journal entry."));
        }
    }

    // ── HasActiveLeaseAsync ───────────────────────────────────────────────────

    public async Task<Result<bool, Error>> HasActiveLeaseAsync(
        string workerId,
        CancellationToken ct = default)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_journalRoot, "*.json"))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var json  = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                    var entry = JsonSerializer.Deserialize<RunJournalEntry>(json, SerializerOptions);
                    if (entry is not null
                        && string.Equals(entry.WorkerId, workerId, StringComparison.Ordinal)
                        && !entry.IsCompleted)
                        return Result.Ok<bool>(true);
                }
                catch (Exception fileEx)
                {
                    _logger.LogWarning(fileEx, "Skipping corrupt journal file '{File}' during lease scan.",
                        Path.GetFileName(file));
                }
            }
            return Result.Ok<bool>(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scan journal directory for active leases.");
            return Result.Fail<bool>(Error.General("Journal.ScanFailure", "Failed to scan journal for active leases."));
        }
    }

    // ── RecordStateAsync (TER-US-005) ─────────────────────────────────────────

    public async Task<Result<bool, Error>> RecordStateAsync(
        string runId,
        string attemptId,
        string leaseId,
        RunState state,
        CancellationToken ct = default)
    {
        try
        {
            var filePath = ResolveAndValidatePath(runId);
            RunJournalEntry entry;
            if (File.Exists(filePath))
            {
                var existingJson = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
                var existing     = JsonSerializer.Deserialize<RunJournalEntry>(existingJson, SerializerOptions);
                if (existing is null)
                    return Result.Fail<bool>(Error.General("Journal.CorruptEntry", "Existing journal entry could not be parsed."));
                // Idempotent: same attemptId + same state = no-op.
                if (string.Equals(existing.AttemptId, attemptId, StringComparison.Ordinal) && existing.State == state)
                    return Result.Ok<bool>(true);
                entry = existing with { State = state, AttemptId = attemptId, LeaseId = leaseId };
            }
            else
            {
                entry = new RunJournalEntry(
                    RunId:            runId,
                    AttemptId:        attemptId,
                    LeaseId:          leaseId,
                    WorkerId:         string.Empty,
                    HeartbeatSeq:     0L,
                    CheckpointAt:     DateTimeOffset.UtcNow,
                    FinalResult:      null,
                    IdempotencyClass: IdempotencyClass.Idempotent,
                    State:            state);
            }
            var json = JsonSerializer.Serialize(entry, SerializerOptions);
            await File.WriteAllTextAsync(filePath, json, ct).ConfigureAwait(false);
            _logger.LogDebug("State recorded for run {RunId} attempt {AttemptId}: {State}.", runId, attemptId, state);
            return Result.Ok<bool>(true);
        }
        catch (UnauthorizedAccessException uae)
        {
            _logger.LogError("Journal path validation failed: {Reason}", uae.Message);
            return Result.Fail<bool>(Error.General("Journal.PathViolation", "Journal path is outside the approved root."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record state for run {RunId}.", runId);
            return Result.Fail<bool>(Error.General("Journal.WriteFailure", "Failed to persist run state."));
        }
    }

    // ── GetStateAsync (TER-US-005) ────────────────────────────────────────────

    public async Task<Result<RunState, Error>> GetStateAsync(
        string runId,
        CancellationToken ct = default)
    {
        var readResult = await ReadEntryAsync(runId, ct).ConfigureAwait(false);
        if (readResult.IsFailure)
            return Result.Fail<RunState>(readResult.Error);
        if (readResult.Value is null)
            return Result.Fail<RunState>(Error.General("Journal.NotFound", $"No journal entry found for run '{runId}'."));
        return Result.Ok<RunState>(readResult.Value.State);
    }

    // ── RecordCheckpointAsync (TER-US-005) ───────────────────────────────────

    public async Task<Result<bool, Error>> RecordCheckpointAsync(
        string runId,
        string attemptId,
        string checkpointName,
        string payload,
        CancellationToken ct = default)
    {
        try
        {
            var filePath = ResolveAndValidatePath(runId);
            if (!File.Exists(filePath))
                return Result.Fail<bool>(Error.General("Journal.NotFound", $"No journal entry found for run '{runId}'."));
            var json     = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
            var existing = JsonSerializer.Deserialize<RunJournalEntry>(json, SerializerOptions);
            if (existing is null)
                return Result.Fail<bool>(Error.General("Journal.CorruptEntry", "Journal entry could not be parsed."));
            var updated     = existing with { CheckpointAt = DateTimeOffset.UtcNow };
            var updatedJson = JsonSerializer.Serialize(updated, SerializerOptions);
            await File.WriteAllTextAsync(filePath, updatedJson, ct).ConfigureAwait(false);
            _logger.LogDebug("Checkpoint '{CheckpointName}' recorded for run {RunId} attempt {AttemptId}.",
                checkpointName, runId, attemptId);
            return Result.Ok<bool>(true);
        }
        catch (UnauthorizedAccessException uae)
        {
            _logger.LogError("Journal path validation failed: {Reason}", uae.Message);
            return Result.Fail<bool>(Error.General("Journal.PathViolation", "Journal path is outside the approved root."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record checkpoint for run {RunId}.", runId);
            return Result.Fail<bool>(Error.General("Journal.WriteFailure", "Failed to persist checkpoint."));
        }
    }

    // ── RecordHeartbeatAsync (TER-US-005) ────────────────────────────────────

    public async Task<Result<bool, Error>> RecordHeartbeatAsync(
        string runId,
        string attemptId,
        int sequence,
        CancellationToken ct = default)
    {
        try
        {
            var filePath = ResolveAndValidatePath(runId);
            if (!File.Exists(filePath))
                return Result.Fail<bool>(Error.General("Journal.NotFound", $"No journal entry found for run '{runId}'."));
            var json     = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
            var existing = JsonSerializer.Deserialize<RunJournalEntry>(json, SerializerOptions);
            if (existing is null)
                return Result.Fail<bool>(Error.General("Journal.CorruptEntry", "Journal entry could not be parsed."));
            // Idempotent: heartbeat sequence must advance monotonically.
            if (existing.HeartbeatSeq >= sequence)
                return Result.Ok<bool>(true);
            var updated     = existing with { HeartbeatSeq = sequence };
            var updatedJson = JsonSerializer.Serialize(updated, SerializerOptions);
            await File.WriteAllTextAsync(filePath, updatedJson, ct).ConfigureAwait(false);
            return Result.Ok<bool>(true);
        }
        catch (UnauthorizedAccessException uae)
        {
            _logger.LogError("Journal path validation failed: {Reason}", uae.Message);
            return Result.Fail<bool>(Error.General("Journal.PathViolation", "Journal path is outside the approved root."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record heartbeat for run {RunId}.", runId);
            return Result.Fail<bool>(Error.General("Journal.WriteFailure", "Failed to persist heartbeat."));
        }
    }

    // ── AppendOutputArtifactRefAsync (TER-US-014) ────────────────────────────

    public async Task<Result<bool, Error>> AppendOutputArtifactRefAsync(
        string runId,
        string attemptId,
        string canonicalPath,
        string sha256Hex,
        CancellationToken ct = default)
    {
        try
        {
            var filePath = ResolveAndValidatePath(runId);
            if (!File.Exists(filePath))
                return Result.Fail<bool>(Error.General("Journal.NotFound", $"No journal entry found for run '{runId}'."));

            var json     = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
            var existing = JsonSerializer.Deserialize<RunJournalEntry>(json, SerializerOptions);
            if (existing is null)
                return Result.Fail<bool>(Error.General("Journal.CorruptEntry", "Journal entry could not be parsed."));

            var newRef  = $"{canonicalPath}|sha256:{sha256Hex}";
            var updated = existing with
            {
                OutputArtifactRefs = [.. existing.OutputArtifactRefs, newRef],
                CheckpointAt       = DateTimeOffset.UtcNow
            };
            var updatedJson = JsonSerializer.Serialize(updated, SerializerOptions);
            await File.WriteAllTextAsync(filePath, updatedJson, ct).ConfigureAwait(false);
            _logger.LogDebug(
                "Output artifact ref recorded for run {RunId} attempt {AttemptId}: sha256={Sha256}.",
                runId, attemptId, sha256Hex);
            return Result.Ok<bool>(true);
        }
        catch (UnauthorizedAccessException uae)
        {
            _logger.LogError("Journal path validation failed: {Reason}", uae.Message);
            return Result.Fail<bool>(Error.General("Journal.PathViolation", "Journal path is outside the approved root."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record output artifact ref for run {RunId}.", runId);
            return Result.Fail<bool>(Error.General("Journal.WriteFailure", "Failed to persist output artifact ref."));
        }
    }

    // ── RecordWslOutageAsync (TER-US-017) ────────────────────────────────────

    public async Task<Result<bool, Error>> RecordWslOutageAsync(
        string runId,
        string attemptId,
        string leaseId,
        WslOutageEvent outageEvent,
        CancellationToken ct = default)
    {
        try
        {
            var filePath = ResolveAndValidatePath(runId);
            RunJournalEntry entry;
            if (File.Exists(filePath))
            {
                var existingJson = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
                var existing     = JsonSerializer.Deserialize<RunJournalEntry>(existingJson, SerializerOptions);
                if (existing is null)
                    return Result.Fail<bool>(Error.General("Journal.CorruptEntry", "Existing journal entry could not be parsed."));

                // Idempotency (CEC invariant #3): if this (attemptId, OccurredAt) pair was
                // already written, return success without a second append.
                bool alreadyRecorded =
                    string.Equals(existing.AttemptId, attemptId, StringComparison.Ordinal)
                    && existing.WslOutageEvents.Any(e => e.OccurredAt == outageEvent.OccurredAt);
                if (alreadyRecorded)
                {
                    _logger.LogDebug(
                        "Duplicate WSL outage event for run {RunId} attempt {AttemptId} suppressed (idempotent).",
                        runId, attemptId);
                    return Result.Ok<bool>(true);
                }

                entry = existing with
                {
                    AttemptId       = attemptId,
                    LeaseId         = leaseId,
                    CheckpointAt    = DateTimeOffset.UtcNow,
                    WslOutageEvents = [.. existing.WslOutageEvents, outageEvent]
                };
            }
            else
            {
                // No existing entry — require one to exist so that state management
                // stays exclusively in RecordStateAsync / RecordUnknownStateAsync.
                // Return an error rather than synthesising a new entry with a
                // hard-coded state that may contradict the intended classification.
                _logger.LogWarning(
                    "RecordWslOutageAsync called for run {RunId} but no journal entry exists.", runId);
                return Result.Fail<bool>(Error.General(
                    "Journal.NotFound",
                    $"No journal entry found for run '{runId}'. Cannot record WSL outage."));
            }

            var json = JsonSerializer.Serialize(entry, SerializerOptions);
            await File.WriteAllTextAsync(filePath, json, ct).ConfigureAwait(false);
            _logger.LogDebug(
                "WSL outage event recorded for run {RunId} attempt {AttemptId}; " +
                "WasSideEffectCommitted={WasSideEffectCommitted}.",
                runId, attemptId, outageEvent.WasSideEffectCommitted);
            return Result.Ok<bool>(true);
        }
        catch (UnauthorizedAccessException uae)
        {
            _logger.LogError("Journal path validation failed: {Reason}", uae.Message);
            return Result.Fail<bool>(Error.General("Journal.PathViolation", "Journal path is outside the approved root."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record WSL outage event for run {RunId}.", runId);
            return Result.Fail<bool>(Error.General("Journal.WriteFailure", "Failed to persist WSL outage event."));
        }
    }

    // ── RecordUnknownStateAsync (TER-US-017) ─────────────────────────────────

    public async Task<Result<bool, Error>> RecordUnknownStateAsync(
        string runId,
        string attemptId,
        string leaseId,
        string unknownReason,
        CancellationToken ct = default)
    {
        try
        {
            var filePath = ResolveAndValidatePath(runId);
            RunJournalEntry entry;
            if (File.Exists(filePath))
            {
                var existingJson = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
                var existing     = JsonSerializer.Deserialize<RunJournalEntry>(existingJson, SerializerOptions);
                if (existing is null)
                    return Result.Fail<bool>(Error.General("Journal.CorruptEntry", "Existing journal entry could not be parsed."));

                // Idempotent: same attemptId + Unknown + same reason = no-op.
                if (string.Equals(existing.AttemptId, attemptId, StringComparison.Ordinal)
                    && existing.State == RunState.Unknown
                    && string.Equals(existing.UnknownReason, unknownReason, StringComparison.Ordinal))
                    return Result.Ok<bool>(true);

                entry = existing with
                {
                    State          = RunState.Unknown,
                    AttemptId      = attemptId,
                    LeaseId        = leaseId,
                    NeedsAttention = true,
                    UnknownReason  = unknownReason,
                    CheckpointAt   = DateTimeOffset.UtcNow
                };
            }
            else
            {
                entry = new RunJournalEntry(
                    RunId:            runId,
                    AttemptId:        attemptId,
                    LeaseId:          leaseId,
                    WorkerId:         string.Empty,
                    HeartbeatSeq:     0L,
                    CheckpointAt:     DateTimeOffset.UtcNow,
                    FinalResult:      null,
                    IdempotencyClass: IdempotencyClass.Irreversible,
                    State:            RunState.Unknown)
                {
                    NeedsAttention = true,
                    UnknownReason  = unknownReason
                };
            }

            var json = JsonSerializer.Serialize(entry, SerializerOptions);
            await File.WriteAllTextAsync(filePath, json, ct).ConfigureAwait(false);
            _logger.LogDebug(
                "UNKNOWN state recorded for run {RunId} attempt {AttemptId}: {UnknownReason}.",
                runId, attemptId, unknownReason);
            return Result.Ok<bool>(true);
        }
        catch (UnauthorizedAccessException uae)
        {
            _logger.LogError("Journal path validation failed: {Reason}", uae.Message);
            return Result.Fail<bool>(Error.General("Journal.PathViolation", "Journal path is outside the approved root."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record UNKNOWN state for run {RunId}.", runId);
            return Result.Fail<bool>(Error.General("Journal.WriteFailure", "Failed to persist UNKNOWN state."));
        }
    }

    // ── HasProcessedCallbackAsync / RecordProcessedCallbackAsync (TER-US-019) ─

    /// <summary>
    /// Returns true when the given callbackId has already been processed for
    /// the specified source run. Persists the deduplication marker as a sidecar
    /// file alongside the run's journal entry.
    /// </summary>
    public Task<Result<bool, Error>> HasProcessedCallbackAsync(
        string sourceRunId,
        string callbackId,
        CancellationToken ct = default)
    {
        try
        {
            var markerPath = ResolveCallbackMarkerPath(sourceRunId, callbackId);
            var exists     = File.Exists(markerPath);
            return Task.FromResult(Result.Ok<bool>(exists));
        }
        catch (UnauthorizedAccessException uae)
        {
            _logger.LogError("Callback marker path validation failed: {Reason}", uae.Message);
            return Task.FromResult(
                Result.Fail<bool>(Error.General("Journal.PathViolation", "Callback marker path is outside the approved root.")));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check callback deduplication marker for run {RunId}.", sourceRunId);
            return Task.FromResult(
                Result.Fail<bool>(Error.General("Journal.ReadFailure", "Failed to check callback deduplication marker.")));
        }
    }

    /// <summary>
    /// Records that the given callbackId has been processed for the specified source run.
    /// Writes a zero-byte sidecar marker file so a crash-then-retry cannot produce a
    /// second Run B (CEC invariant #3, TER-US-019 AC-3).
    /// </summary>
    public async Task<Result<bool, Error>> RecordProcessedCallbackAsync(
        string sourceRunId,
        string callbackId,
        CancellationToken ct = default)
    {
        try
        {
            var markerPath = ResolveCallbackMarkerPath(sourceRunId, callbackId);
            await File.WriteAllBytesAsync(markerPath, [], ct).ConfigureAwait(false);
            _logger.LogDebug(
                "Callback deduplication marker recorded for run {RunId} callbackId {CallbackId}.",
                sourceRunId, callbackId);
            return Result.Ok<bool>(true);
        }
        catch (UnauthorizedAccessException uae)
        {
            _logger.LogError("Callback marker path validation failed: {Reason}", uae.Message);
            return Result.Fail<bool>(Error.General("Journal.PathViolation", "Callback marker path is outside the approved root."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record callback deduplication marker for run {RunId}.", sourceRunId);
            return Result.Fail<bool>(Error.General("Journal.WriteFailure", "Failed to persist callback deduplication marker."));
        }
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private string ResolveAndValidatePath(string runId)
    {
        var safeName = Path.GetFileName(runId);
        if (string.IsNullOrWhiteSpace(safeName))
            throw new UnauthorizedAccessException("RunId produced an empty file name.");
        var fullPath = Path.GetFullPath(Path.Combine(_journalRoot, $"{safeName}.json"));
        if (!fullPath.StartsWith(_journalRoot, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                $"Resolved path is outside the approved journal root.");
        return fullPath;
    }

    /// <summary>
    /// Resolves the sidecar deduplication marker path for a callback.
    /// Format: {journalRoot}/{safeRunId}.cb.{safeCallbackId}.marker
    /// Both segments are validated against the journal root.
    /// </summary>
    private string ResolveCallbackMarkerPath(string sourceRunId, string callbackId)
    {
        var safeRunId      = Path.GetFileName(sourceRunId);
        var safeCallbackId = Path.GetFileName(callbackId);
        if (string.IsNullOrWhiteSpace(safeRunId) || string.IsNullOrWhiteSpace(safeCallbackId))
            throw new UnauthorizedAccessException("RunId or callbackId produced an empty file name segment.");
        var fullPath = Path.GetFullPath(
            Path.Combine(_journalRoot, $"{safeRunId}.cb.{safeCallbackId}.marker"));
        if (!fullPath.StartsWith(_journalRoot, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Callback marker path is outside the approved journal root.");
        return fullPath;
    }
}
