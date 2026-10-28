using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain;
using Tracto.TerminalRuntime.Domain.Process;
using Microsoft.Extensions.Logging;

namespace Tracto.TerminalRuntime.Application.Process;

/// <summary>
/// Orchestrates the full execution lifecycle of a terminal process:
/// start → await (with deadline enforcement) → state transition.
///
/// This handler covers TER-US-015 AC-2 (await), AC-4 (deadline), and the
/// run-state transitions to SUCCEEDED, FAILED, TIMED_OUT, and CANCELLED.
///
/// Callers that wish to cancel must use <see cref="IProcessController.CancelAsync"/>
/// directly and then call <see cref="RecordCancelledAsync"/> to persist the state.
/// No business-logic predicates live here (CEC invariant #7).
/// No plaintext secrets in logs (CEC invariant #4).
/// </summary>
public sealed class ProcessExecutionHandler(
    IProcessController controller,
    IRunJournal journal,
    ILogger<ProcessExecutionHandler> logger)
{
    /// <summary>
    /// Awaits the process tree and transitions the run to its terminal state.
    /// Returns the <see cref="ProcessOutcome"/> for the caller to act on.
    /// </summary>
    public async Task<Result<ProcessOutcome, Error>> AwaitAndTransitionAsync(
        AwaitProcessCommand command,
        CancellationToken ct = default)
    {
        var awaitResult = await controller.AwaitAsync(command, ct);
        if (awaitResult.IsFailure)
        {
            logger.LogError("Run {RunId} attempt {AttemptId}: AwaitAsync failed: {Code}",
                command.RunId, command.AttemptId, awaitResult.Error.Code);
            return awaitResult.Error;
        }

        var outcome = awaitResult.Value;
        RunState nextState = DetermineNextState(outcome);

        var stateResult = await journal.RecordStateAsync(
            command.RunId,
            command.AttemptId,
            leaseId: string.Empty,
            nextState,
            ct);

        if (stateResult.IsFailure)
        {
            logger.LogError(
                "Run {RunId}: could not persist state {State}: {Code}",
                command.RunId, nextState, stateResult.Error.Code);
            // Non-fatal: outcome is still valid; caller will surface via heartbeat.
        }

        logger.LogInformation(
            "Run {RunId} attempt {AttemptId}: process completed. " +
            "TimedOut={TimedOut} Cancelled={Cancelled} ExitCode={ExitCode} => {State}",
            command.RunId, command.AttemptId,
            outcome.TimedOut, outcome.Cancelled, outcome.ExitCode, nextState);

        return outcome;
    }

    /// <summary>
    /// Records the CANCELLED state after a successful <see cref="IProcessController.CancelAsync"/>
    /// call that the caller has already executed.
    /// </summary>
    public async Task<Result<bool, Error>> RecordCancelledAsync(
        string runId,
        string attemptId,
        CancellationToken ct = default)
    {
        var stateResult = await journal.RecordStateAsync(
            runId, attemptId, leaseId: string.Empty, RunState.Cancelled, ct);

        if (stateResult.IsFailure)
        {
            logger.LogError("Run {RunId}: could not persist CANCELLED state: {Code}",
                runId, stateResult.Error.Code);
        }

        return stateResult;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static RunState DetermineNextState(ProcessOutcome outcome)
    {
        if (outcome.TimedOut)    return RunState.TimedOut;
        if (outcome.Cancelled)   return RunState.Cancelled;
        if (outcome.ExitCode == 0) return RunState.Succeeded;
        return RunState.Failed;
    }
}
