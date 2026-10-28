using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Worker;

/// <summary>
/// CEC-004: File-backed run journal.
/// Persists runId, steps, heartbeat checkpoints, and final run state to the local filesystem
/// so that the reconciliation service can determine run progress after an Orchestrator or
/// worker restart — without replaying irreversible side effects.
///
/// Thread-safety: each run's in-memory list is guarded by its own lock before snapshot.
/// Disk writes are serialised per-run by the sequential nature of step execution.
/// </summary>
public sealed class RunJournal(string journalDirectory, ILogger<RunJournal> logger) : IRunJournal
{
    private readonly ConcurrentDictionary<string, List<JournalEntry>> _memoryJournal = new();

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };

    // ── IRunJournal ──────────────────────────────────────────────────────────

    public async Task OpenAsync(string runId, CancellationToken ct)
    {
        var entries = _memoryJournal.GetOrAdd(runId, _ => []);
        var entry = new JournalEntry
        {
            StepId  = "journal.open",
            Action  = "open",
            Timestamp = DateTimeOffset.UtcNow,
            Outcome = "opened"
        };
        lock (entries)
            entries.Add(entry);

        await PersistAsync(runId, ct).ConfigureAwait(false);
        logger.LogDebug("Journal opened — runId={RunId}", runId);
    }

    public async Task AppendStepAsync(
        string runId,
        string stepId,
        string action,
        object? outcome,
        CancellationToken ct)
    {
        var entries = _memoryJournal.GetOrAdd(runId, _ => []);
        var entry = new JournalEntry
        {
            StepId    = stepId,
            Action    = action,
            Timestamp = DateTimeOffset.UtcNow,
            Outcome   = outcome
        };
        lock (entries)
            entries.Add(entry);

        await PersistAsync(runId, ct).ConfigureAwait(false);
    }

    public async Task CloseAsync(string runId, RunState finalState, CancellationToken ct)
    {
        var entries = _memoryJournal.GetOrAdd(runId, _ => []);
        // Store final state as a plain string so ReconciliationService can parse it
        // after a disk-read without ambiguity.
        var closeEntry = new JournalEntry
        {
            StepId    = "journal.close",
            Action    = "close",
            Timestamp = DateTimeOffset.UtcNow,
            Outcome   = finalState.ToString()
        };
        lock (entries)
            entries.Add(closeEntry);

        await PersistAsync(runId, ct).ConfigureAwait(false);
        logger.LogInformation(
            "Journal closed — runId={RunId}, finalState={State}", runId, finalState);
    }

    public async Task<IReadOnlyList<JournalEntry>> ReadAsync(string runId, CancellationToken ct)
    {
        // Prefer the in-memory journal (hot path)
        if (_memoryJournal.TryGetValue(runId, out var memEntries))
        {
            lock (memEntries)
                return [.. memEntries];
        }

        // Reconciliation path — run was loaded from disk after a restart
        var path = JournalPath(runId);
        if (!File.Exists(path))
            return [];

        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var persisted = JsonSerializer.Deserialize<List<PersistedEntry>>(json, _jsonOptions) ?? [];

        return persisted.Select(e => new JournalEntry
        {
            StepId    = e.StepId,
            Action    = e.Action,
            Timestamp = e.Timestamp,
            Outcome   = e.Outcome    // string? survives the round-trip as object?
        }).ToList();
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private string JournalPath(string runId)
        => Path.Combine(journalDirectory, $"{runId}.journal.json");

    private async Task PersistAsync(string runId, CancellationToken ct)
    {
        if (!_memoryJournal.TryGetValue(runId, out var entries))
            return;

        List<JournalEntry> snapshot;
        lock (entries)
            snapshot = [.. entries];

        var persisted = snapshot.Select(e => new PersistedEntry
        {
            StepId    = e.StepId,
            Action    = e.Action,
            Timestamp = e.Timestamp,
            // Serialize structured outcome objects to JSON; plain strings pass through
            Outcome   = e.Outcome is string s ? s : JsonSerializer.Serialize(e.Outcome, _jsonOptions)
        }).ToList();

        Directory.CreateDirectory(journalDirectory);
        var json = JsonSerializer.Serialize(persisted, _jsonOptions);
        await File.WriteAllTextAsync(JournalPath(runId), json, ct).ConfigureAwait(false);
    }

    // Private record used only for file serialisation — not exposed via the interface
    private sealed record PersistedEntry
    {
        public required string StepId    { get; init; }
        public required string Action    { get; init; }
        public required DateTimeOffset Timestamp { get; init; }
        public string? Outcome           { get; init; }
    }
}
