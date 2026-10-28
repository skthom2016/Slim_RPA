using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain;

namespace Tracto.TerminalRuntime.Application;

// ---------------------------------------------------------------------------
// Result
// ---------------------------------------------------------------------------

/// <summary>
/// The outcome of a reconciliation probe for a run that experienced a lease
/// expiry (e.g. after an Orchestrator restart or network partition).
/// </summary>
public sealed record ReconcileRunResult(
    RunJournalEntry Entry,

    /// <summary>
    /// True only when the last active command step was <see cref="IdempotencyClass.Idempotent"/>
    /// and the run has not yet reached a terminal state.
    ///
    /// When false the Orchestrator must NOT replay the command; it should
    /// instead surface the run to a human operator with <c>needsAttention=true</c>
    /// (CEC v1.0.1 invariants #2 and #3).
    /// </summary>
    bool ShouldReplay);

// ---------------------------------------------------------------------------
// Handler
// ---------------------------------------------------------------------------

/// <summary>
/// Reconciles a run after lease expiry or Orchestrator restart (TER-US-022, AC-3 and AC-4).
///
/// Key rule: <b>irreversible commands are never auto-replayed</b>.
/// If the journal shows the last active step was <see cref="IdempotencyClass.Irreversible"/>,
/// <see cref="ReconcileRunResult.ShouldReplay"/> is set to <c>false</c> and the
/// caller is responsible for escalating to a human operator.
///
/// No process-launch or network logic lives here (CEC invariant #5).
/// </summary>
public sealed class ReconcileRunHandler(IRunJournal journal)
{
    public async Task<Result<ReconcileRunResult, Error>> HandleAsync(
        string runId,
        CancellationToken ct = default)
    {
        var readResult = await journal.ReadEntryAsync(runId, ct);
        if (readResult.IsFailure)
            return Result<ReconcileRunResult, Error>.Fail(readResult.Error);

        var entry = readResult.Value;
        if (entry is null)
            return Result<ReconcileRunResult, Error>.Fail(Error.General(
                "Reconcile.NotFound",
                $"No journal entry found for run '{runId}'. Cannot reconcile."));

        // AC-3 / AC-4: never replay irreversible commands; never replay a run
        // that has already reached a terminal state; never replay a run in the
        // UNKNOWN state (CEC v1.0.1 invariant #2 — UNKNOWN runs always require
        // human attention and must never be auto-replayed regardless of class).
        bool shouldReplay =
            !entry.IsCompleted &&
            entry.State != RunState.Unknown &&
            entry.IdempotencyClass == IdempotencyClass.Idempotent;

        return Result<ReconcileRunResult, Error>.Ok(new ReconcileRunResult(entry, shouldReplay));
    }
}
