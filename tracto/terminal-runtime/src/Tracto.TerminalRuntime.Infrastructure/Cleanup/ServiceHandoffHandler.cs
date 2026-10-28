using Microsoft.Extensions.Logging;
using Tracto.TerminalRuntime.Application;
using Tracto.TerminalRuntime.Application.Cleanup;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.Interfaces;
using Tracto.TerminalRuntime.Domain;
using Tracto.TerminalRuntime.Domain.Cleanup;

namespace Tracto.TerminalRuntime.Infrastructure.Cleanup;

/// <summary>
/// Persists the HANDED_OFF state transition in the run journal and releases
/// the Terminal worker.
///
/// CEC v1.0.1 AC-3 invariants enforced here:
/// <list type="bullet">
///   <item>HANDED_OFF state is written to the journal before this method returns.</item>
///   <item>No continuation run (Run B) is created here — that is deferred to the
///         callback handler.</item>
///   <item>No plaintext secrets are written to the journal entry (CEC invariant #4).</item>
/// </list>
/// </summary>
public sealed class ServiceHandoffHandler(
    IRunJournal journal,
    IClock clock,
    ILogger<ServiceHandoffHandler> logger) : IServiceHandoffHandler
{
    public async Task<Result<HandoffResult, Error>> HandleHandoffAsync(
        string runId,
        string attemptId,
        string leaseId,
        string? continuationMappingRef,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(runId))
            return Result<HandoffResult, Error>.Fail(Error.Validation(
                "Handoff.RunIdRequired", "runId must not be null or whitespace."));

        if (string.IsNullOrWhiteSpace(attemptId))
            return Result<HandoffResult, Error>.Fail(Error.Validation(
                "Handoff.AttemptIdRequired", "attemptId must not be null or whitespace."));

        if (string.IsNullOrWhiteSpace(leaseId))
            return Result<HandoffResult, Error>.Fail(Error.Validation(
                "Handoff.LeaseIdRequired", "leaseId must not be null or whitespace."));

        var handedOffAt = clock.UtcNow;

        // Persist HANDED_OFF state transition to the journal.
        var stateResult = await journal.RecordStateAsync(
            runId, attemptId, leaseId, RunState.HandedOff, ct);

        if (stateResult.IsFailure)
        {
            logger.LogError(
                "Failed to persist HANDED_OFF state for run {RunId}: {Code}",
                runId, stateResult.Error.Code);
            return Result<HandoffResult, Error>.Fail(stateResult.Error);
        }

        logger.LogInformation(
            "Run {RunId} transitioned to HANDED_OFF. Terminal worker released. " +
            "Continuation will be created only upon eligible callback.",
            runId);

        return Result<HandoffResult, Error>.Ok(new HandoffResult(
            RunId:                  runId,
            HandedOffAt:            handedOffAt,
            ContinuationMappingRef: continuationMappingRef));
    }
}
