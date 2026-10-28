using Tracto.TerminalRuntime.Application.Cleanup;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain;
using Tracto.TerminalRuntime.Domain.Service;

namespace Tracto.TerminalRuntime.Application.Service;

// ---------------------------------------------------------------------------
// Command
// ---------------------------------------------------------------------------

/// <summary>
/// Carries the result of a service.invoke call and the fencing identifiers
/// needed to write the run-state transition to the journal.
/// </summary>
/// <param name="RunId">Unique identifier of the logical run.</param>
/// <param name="AttemptId">Identifier of the current execution attempt (AttemptId fence).</param>
/// <param name="LeaseId">Identifier of the lease that owns this run.</param>
/// <param name="InvokeResult">
///   The typed result returned by <see cref="IServiceInvokeClient.InvokeAsync"/>.
/// </param>
/// <param name="ContinuationMappingRef">
///   The statically-configured continuation mapping reference to record when the
///   outcome is <see cref="ServiceOutcome.AsyncAcknowledged"/>.
///   Null when no continuation is configured.
/// </param>
public sealed record HandleServiceOutcomeCommand(
    string              RunId,
    string              AttemptId,
    string              LeaseId,
    ServiceInvokeResult InvokeResult,
    string?             ContinuationMappingRef);

// ---------------------------------------------------------------------------
// Result
// ---------------------------------------------------------------------------

/// <summary>
/// The outcome of running <see cref="HandleServiceOutcomeCommand"/> through the handler.
/// </summary>
/// <param name="FinalRunState">
///   The CEC v1.0.1 run state the attempt was transitioned to.
/// </param>
/// <param name="BusinessOutcome">
///   "REJECTED" when the service outcome was <see cref="ServiceOutcome.BusinessRejection"/>;
///   null for all other outcomes.
/// </param>
/// <param name="NeedsAttention">
///   True when <see cref="FinalRunState"/> is <see cref="RunState.Unknown"/>
///   (CEC v1.0.1 invariant #2).
/// </param>
/// <param name="UnknownReason">
///   A human-readable code explaining the UNKNOWN state.
///   Non-null only when <see cref="NeedsAttention"/> is true.
/// </param>
public sealed record HandleServiceOutcomeResult(
    RunState FinalRunState,
    string?  BusinessOutcome,
    bool     NeedsAttention,
    string?  UnknownReason);

// ---------------------------------------------------------------------------
// Handler
// ---------------------------------------------------------------------------

/// <summary>
/// Maps a <see cref="ServiceInvokeResult"/> to the correct CEC v1.0.1 run state
/// and records the transition in the journal.
///
/// Branching rules (CEC v1.0.1 invariant #6 — branch only on outcome category or
/// approved actionCode, never on domain fields):
/// <list type="bullet">
///   <item>SUCCEEDED         → SUCCEEDED</item>
///   <item>FAILED            → FAILED</item>
///   <item>BUSINESS_REJECTION → SUCCEEDED + businessOutcome="REJECTED" (no retry)</item>
///   <item>UNKNOWN_OUTCOME   → UNKNOWN + needsAttention=true + unknownReason (stops attempt)</item>
///   <item>ASYNC_ACKNOWLEDGED → HANDED_OFF via <see cref="IServiceHandoffHandler"/></item>
/// </list>
///
/// No process-launch or direct network logic lives here (CEC invariant #5).
/// </summary>
public sealed class HandleServiceOutcomeHandler(
    IRunJournal            journal,
    IServiceHandoffHandler handoffHandler) : IServiceOutcomeHandler
{
    /// <summary>Business outcome label used for rejected service calls.</summary>
    private const string BusinessOutcomeRejected = "REJECTED";

    /// <summary>Unknown-reason code for an indeterminate service outcome.</summary>
    private const string UnknownReasonServiceOutcomeIndeterminate =
        "ServiceOutcome.Indeterminate";

    // ── IServiceOutcomeHandler ────────────────────────────────────────────────

    public async Task<Result<HandleServiceOutcomeResult, Error>> HandleAsync(
        HandleServiceOutcomeCommand command,
        CancellationToken ct = default)
    {
        if (command is null)
            throw new ArgumentNullException(nameof(command));

        if (string.IsNullOrWhiteSpace(command.RunId))
            return Result<HandleServiceOutcomeResult, Error>.Fail(Error.Validation(
                "ServiceOutcome.RunIdRequired", "runId must not be null or whitespace."));

        if (string.IsNullOrWhiteSpace(command.AttemptId))
            return Result<HandleServiceOutcomeResult, Error>.Fail(Error.Validation(
                "ServiceOutcome.AttemptIdRequired", "attemptId must not be null or whitespace."));

        if (string.IsNullOrWhiteSpace(command.LeaseId))
            return Result<HandleServiceOutcomeResult, Error>.Fail(Error.Validation(
                "ServiceOutcome.LeaseIdRequired", "leaseId must not be null or whitespace."));

        if (command.InvokeResult is null)
            return Result<HandleServiceOutcomeResult, Error>.Fail(Error.Validation(
                "ServiceOutcome.InvokeResultRequired", "InvokeResult must not be null."));

        // Branch on outcome category only — never on domain-specific fields
        // (CEC v1.0.1 invariant #6).
        return command.InvokeResult.Outcome switch
        {
            ServiceOutcome.Succeeded => await HandleSucceededAsync(command, ct),
            ServiceOutcome.Failed => await HandleFailedAsync(command, ct),
            ServiceOutcome.BusinessRejection => await HandleBusinessRejectionAsync(command, ct),
            ServiceOutcome.UnknownOutcome => await HandleUnknownOutcomeAsync(command, ct),
            ServiceOutcome.AsyncAcknowledged => await HandleAsyncAcknowledgedAsync(command, ct),
            _ => Result<HandleServiceOutcomeResult, Error>.Fail(Error.General(
                "ServiceOutcome.Unrecognised",
                $"Unrecognised service outcome value '{command.InvokeResult.Outcome}'."))
        };
    }

    // ── Private outcome handlers ──────────────────────────────────────────────

    private async Task<Result<HandleServiceOutcomeResult, Error>> HandleSucceededAsync(
        HandleServiceOutcomeCommand command,
        CancellationToken ct)
    {
        var stateResult = await journal.RecordStateAsync(
            command.RunId, command.AttemptId, command.LeaseId, RunState.Succeeded, ct);

        if (stateResult.IsFailure)
            return Result<HandleServiceOutcomeResult, Error>.Fail(stateResult.Error);

        return Result<HandleServiceOutcomeResult, Error>.Ok(new HandleServiceOutcomeResult(
            FinalRunState:  RunState.Succeeded,
            BusinessOutcome: null,
            NeedsAttention:  false,
            UnknownReason:   null));
    }

    private async Task<Result<HandleServiceOutcomeResult, Error>> HandleFailedAsync(
        HandleServiceOutcomeCommand command,
        CancellationToken ct)
    {
        var stateResult = await journal.RecordStateAsync(
            command.RunId, command.AttemptId, command.LeaseId, RunState.Failed, ct);

        if (stateResult.IsFailure)
            return Result<HandleServiceOutcomeResult, Error>.Fail(stateResult.Error);

        return Result<HandleServiceOutcomeResult, Error>.Ok(new HandleServiceOutcomeResult(
            FinalRunState:  RunState.Failed,
            BusinessOutcome: null,
            NeedsAttention:  false,
            UnknownReason:   null));
    }

    /// <summary>
    /// BUSINESS_REJECTION → run state SUCCEEDED; businessOutcome="REJECTED".
    ///
    /// AC-2: A business rejection is a valid, expected business outcome — not a
    /// technical failure. The run transitions to SUCCEEDED so that downstream
    /// orchestration is not polluted with spurious failures. If a separate
    /// post-rejection handling step fails for an independent technical reason,
    /// that technical failure maps to FAILED for that step — not the business
    /// rejection itself.
    /// </summary>
    private async Task<Result<HandleServiceOutcomeResult, Error>> HandleBusinessRejectionAsync(
        HandleServiceOutcomeCommand command,
        CancellationToken ct)
    {
        // Record SUCCEEDED state (not FAILED — business rejections are expected outcomes).
        var stateResult = await journal.RecordStateAsync(
            command.RunId, command.AttemptId, command.LeaseId, RunState.Succeeded, ct);

        if (stateResult.IsFailure)
            return Result<HandleServiceOutcomeResult, Error>.Fail(stateResult.Error);

        return Result<HandleServiceOutcomeResult, Error>.Ok(new HandleServiceOutcomeResult(
            FinalRunState:   RunState.Succeeded,
            BusinessOutcome: BusinessOutcomeRejected,
            NeedsAttention:  false,
            UnknownReason:   null));
    }

    /// <summary>
    /// UNKNOWN_OUTCOME → run state UNKNOWN; needsAttention=true; attempt stopped.
    ///
    /// AC-3: The service could not confirm whether the operation was applied.
    /// The attempt is terminated as UNKNOWN. No continuation is created and the
    /// run must not be auto-replayed (CEC v1.0.1 invariants #2 and #3).
    /// </summary>
    private async Task<Result<HandleServiceOutcomeResult, Error>> HandleUnknownOutcomeAsync(
        HandleServiceOutcomeCommand command,
        CancellationToken ct)
    {
        var unknownResult = await journal.RecordUnknownStateAsync(
            command.RunId,
            command.AttemptId,
            command.LeaseId,
            UnknownReasonServiceOutcomeIndeterminate,
            ct);

        if (unknownResult.IsFailure)
            return Result<HandleServiceOutcomeResult, Error>.Fail(unknownResult.Error);

        return Result<HandleServiceOutcomeResult, Error>.Ok(new HandleServiceOutcomeResult(
            FinalRunState:   RunState.Unknown,
            BusinessOutcome: null,
            NeedsAttention:  true,                              // CEC v1.0.1 invariant #2
            UnknownReason:   UnknownReasonServiceOutcomeIndeterminate));
    }

    /// <summary>
    /// ASYNC_ACKNOWLEDGED → run state HANDED_OFF; Terminal worker released.
    ///
    /// AC-4: The service accepted the request for async processing.  The Terminal
    /// worker is released immediately (no blocking wait).  A continuation run is
    /// created only when an eligible, authenticated, deduplicated callback arrives.
    /// Dynamic scriptRef values in any future callback are refused.
    /// </summary>
    private async Task<Result<HandleServiceOutcomeResult, Error>> HandleAsyncAcknowledgedAsync(
        HandleServiceOutcomeCommand command,
        CancellationToken ct)
    {
        var handoffResult = await handoffHandler.HandleHandoffAsync(
            command.RunId,
            command.AttemptId,
            command.LeaseId,
            command.ContinuationMappingRef,
            ct);

        if (handoffResult.IsFailure)
            return Result<HandleServiceOutcomeResult, Error>.Fail(handoffResult.Error);

        return Result<HandleServiceOutcomeResult, Error>.Ok(new HandleServiceOutcomeResult(
            FinalRunState:   RunState.HandedOff,
            BusinessOutcome: null,
            NeedsAttention:  false,
            UnknownReason:   null));
    }
}
