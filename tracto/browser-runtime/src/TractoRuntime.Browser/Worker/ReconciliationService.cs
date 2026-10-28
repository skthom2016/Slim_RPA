using Microsoft.Extensions.Logging;
using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Worker;

/// <summary>
/// CEC-004 / CEC-HOST-003: Reconciliation after Orchestrator or worker restart.
///
/// After a disconnect or unexpected worker exit the runtime must not assume failure and
/// must not automatically replay irreversible actions.  This service reads the persisted
/// run journal and returns a <see cref="ReconciliationResult"/> that tells the caller:
/// <list type="bullet">
///   <item>Whether the run was already closed (and what the final state was)</item>
///   <item>Whether the run was interrupted mid-flight</item>
///   <item>Whether any effectful action had already been committed</item>
/// </list>
/// The caller uses this information to decide whether it is safe to reuse an existing
/// browser session or whether a new attempt must be started (and if so, at which
/// checkpoint it may safely resume without duplicating a committed side effect).
/// </summary>
public sealed class ReconciliationService(IRunJournal journal, ILogger<ReconciliationService> logger)
{
    // Actions that commit an irreversible side effect that must not be blindly replayed
    private static readonly HashSet<string> _effectfulActions =
    [
        "form.submit",
        "click",
        "navigate",
        "file.download",
        "file.upload",
        "service.invoke"
    ];

    /// <summary>
    /// Determines the reconciliation verdict for <paramref name="runId"/> from the persisted journal.
    /// </summary>
    public async Task<ReconciliationResult> ReconcileAsync(string runId, CancellationToken ct)
    {
        var entries = await journal.ReadAsync(runId, ct).ConfigureAwait(false);

        if (entries.Count == 0)
        {
            logger.LogInformation(
                "Reconciliation: no journal found for run {RunId}", runId);
            return new ReconciliationResult
            {
                RunId                = runId,
                Verdict              = ReconciliationVerdict.NoJournalFound,
                LastKnownState       = null,
                HasCommittedSideEffect = false
            };
        }

        // Locate the most recent close entry (Action == "close") if the run finished normally
        var closeEntry = entries.LastOrDefault(e => e.Action == "close");

        RunState? lastState = null;
        if (closeEntry?.Outcome is string stateStr &&
            Enum.TryParse<RunState>(stateStr, ignoreCase: true, out var parsed))
        {
            lastState = parsed;
        }

        // Determine whether any irreversible side effect was committed
        bool hasCommittedSideEffect = entries.Any(e => _effectfulActions.Contains(e.Action));

        var verdict = closeEntry is not null
            ? ReconciliationVerdict.RunAlreadyClosed
            : ReconciliationVerdict.RunInterrupted;

        logger.LogInformation(
            "Reconciliation for run {RunId}: verdict={Verdict}, lastState={LastState}, " +
            "committedSideEffect={Committed}, journalEntries={Count}",
            runId, verdict, lastState, hasCommittedSideEffect, entries.Count);

        return new ReconciliationResult
        {
            RunId                = runId,
            Verdict              = verdict,
            LastKnownState       = lastState,
            HasCommittedSideEffect = hasCommittedSideEffect,
            JournalEntries       = entries
        };
    }
}

/// <summary>Outcome of a run journal reconciliation check.</summary>
public sealed record ReconciliationResult
{
    public required string RunId                        { get; init; }
    public required ReconciliationVerdict Verdict       { get; init; }
    /// <summary>The run's persisted terminal state, or <c>null</c> if the run was interrupted.</summary>
    public RunState? LastKnownState                     { get; init; }
    /// <summary>
    /// <c>true</c> when at least one effectful action (navigate, click, form.submit,
    /// service.invoke, file.download, file.upload) appears in the journal.
    /// The runtime must not blindly replay these actions after a reconnect.
    /// </summary>
    public required bool HasCommittedSideEffect         { get; init; }
    public IReadOnlyList<JournalEntry>? JournalEntries  { get; init; }
}

public enum ReconciliationVerdict
{
    /// <summary>No journal file found — run may not have started, or journal was lost.</summary>
    NoJournalFound,
    /// <summary>Journal present and contains a close entry — run ended before the restart.</summary>
    RunAlreadyClosed,
    /// <summary>Journal present but no close entry — run was interrupted mid-flight.</summary>
    RunInterrupted
}
