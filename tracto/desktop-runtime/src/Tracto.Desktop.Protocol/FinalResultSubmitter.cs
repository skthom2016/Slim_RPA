#nullable enable

using Tracto.Contract.Models;
using Tracto.Contract.Models.Execution;
using Tracto.Contract.Models.Protocol;
using Tracto.Contract.Orchestrator;

namespace Tracto.Desktop.Protocol;

/// <summary>
/// Submits the final execution result to the Orchestrator and releases the lease.
/// UNKNOWN results carry unknownReason and needsAttention=true per CEC v1.0.1.
/// HANDED_OFF sets run state to HandedOff and releases the lease; no further polling.
/// </summary>
public sealed class FinalResultSubmitter
{
    private readonly string _workerId;

    public FinalResultSubmitter(string workerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        _workerId = workerId;
    }

    /// <summary>
    /// Submits the final result envelope to the Orchestrator.
    /// </summary>
    /// <param name="orch">The Orchestrator server.</param>
    /// <param name="context">The active job context containing leaseId and attemptId.</param>
    /// <param name="state">The terminal run state.</param>
    /// <param name="steps">Step results from this run.</param>
    /// <param name="unknownReason">Required when <paramref name="state"/> is <see cref="RunState.Unknown"/>.</param>
    /// <param name="errorCategory">Optional error category.</param>
    /// <param name="errorDetail">Optional stable error detail code.</param>
    /// <param name="startedAt">When execution started.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task SubmitAsync(
        IOrchestratorServer orch,
        JobContext context,
        RunState state,
        IReadOnlyList<StepResult> steps,
        string? unknownReason     = null,
        ErrorCategory? errorCategory = null,
        string? errorDetail       = null,
        DateTimeOffset? startedAt = null,
        CancellationToken ct      = default)
    {
        ArgumentNullException.ThrowIfNull(orch);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(steps);

        var start       = startedAt ?? DateTimeOffset.UtcNow;
        var completed   = DateTimeOffset.UtcNow;
        long durationMs = (long)(completed - start).TotalMilliseconds;

        // UNKNOWN state must carry unknownReason + needsAttention=true (CEC v1.0.1 C3 rule).
        bool? needsAttention = state == RunState.Unknown ? true : null;
        if (state == RunState.Unknown && string.IsNullOrWhiteSpace(unknownReason))
            throw new ArgumentException("unknownReason must be provided when run state is UNKNOWN.", nameof(unknownReason));

        var runResult = new RunResult(
            RunId:        context.RunId,
            AttemptId:    context.AttemptId,
            LeaseId:      context.LeaseId,
            WorkerId:     _workerId,
            RunState:     state,
            StartedAt:    start,
            CompletedAt:  completed,
            DurationMs:   durationMs,
            LastHeartbeatAt: null,
            StepResults:  steps,
            ErrorCategory:  errorCategory,
            ErrorDetail:    errorDetail,
            NeedsAttention: needsAttention,
            UnknownReason:  unknownReason);

        var finalResult = new FinalResult(
            LeaseId:  context.LeaseId,
            WorkerId: _workerId,
            Result:   runResult);

        await orch.ProcessFinalResultAsync(finalResult, ct).ConfigureAwait(false);
    }
}
