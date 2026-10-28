using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain;
using Tracto.TerminalRuntime.Domain.Cleanup;

namespace Tracto.TerminalRuntime.Application.Cleanup;

// ---------------------------------------------------------------------------
// Command
// ---------------------------------------------------------------------------

/// <summary>
/// Represents an inbound callback from a remote service that asynchronously
/// completed work originally handed off by a HANDED_OFF run.
///
/// CEC v1.0.1 AC-3 / AC-4 rules enforced by <see cref="ReceiveCallbackHandler"/>:
/// <list type="bullet">
///   <item>The callback must be authenticated: <see cref="CallbackHmac"/> is
///         verified against a configured secret before any other check.</item>
///   <item>The callback must be deduplicated: <see cref="CallbackId"/> is checked
///         against the source run journal; a duplicate delivery returns an
///         idempotent 200-equivalent result with no second Run B created.</item>
///   <item>The callback must be eligible: the source run must already be in
///         HANDED_OFF state before a continuation run is created.</item>
///   <item>Any <see cref="DynamicScriptRefFromCallback"/> is refused — the
///         continuation's scriptRef is always taken from the static mapping.</item>
///   <item>Run B is NOT created speculatively; it is created only here, after
///         HANDED_OFF is confirmed.</item>
/// </list>
/// </summary>
public sealed record ReceiveCallbackCommand(
    /// <summary>Idempotency key for this specific callback delivery.</summary>
    string CallbackId,

    /// <summary>
    /// HMAC-SHA256 (or equivalent) credential supplied by the remote service.
    /// Verified by <see cref="ICallbackAuthenticator"/> as the very first guard.
    /// The raw value is never logged or persisted (CEC invariant #4).
    /// </summary>
    string CallbackHmac,

    /// <summary>RunId of the source run (the HANDED_OFF run).</summary>
    string SourceRunId,

    /// <summary>
    /// The continuation mapping reference embedded in the source run.
    /// Used to look up the static scriptRef for Run B.
    /// </summary>
    string ContinuationMappingRef,

    /// <summary>
    /// If the callback payload contains a scriptRef, it is captured here so
    /// the handler can detect and refuse dynamic scriptRef injection.
    /// Null when the callback carries no scriptRef (the normal case).
    /// </summary>
    string? DynamicScriptRefFromCallback,

    /// <summary>Opaque payload from the remote service (safe for logging after redaction).</summary>
    string? ServicePayload);

// ---------------------------------------------------------------------------
// Result
// ---------------------------------------------------------------------------

/// <summary>Outcome of processing an inbound service callback.</summary>
public sealed record ReceiveCallbackResult(
    /// <summary>Whether a continuation run (Run B) was created.</summary>
    bool ContinuationRunCreated,

    /// <summary>
    /// Identifier of the continuation run, or null when none was created.
    /// </summary>
    string? ContinuationRunId,

    /// <summary>The scriptRef used for the continuation run (from static mapping).</summary>
    string? ContinuationScriptRef);

// ---------------------------------------------------------------------------
// Handler
// ---------------------------------------------------------------------------

/// <summary>
/// Handles an inbound async callback from a remote service.
///
/// <para>
/// This handler enforces all AC-3 and AC-4 invariants in the following order:
/// <list type="number">
///   <item>Authenticates the inbound callback via <see cref="ICallbackAuthenticator"/>
///         (AC-3: "eligible, authenticated, deduplicated callback"). A spoofed request
///         is refused here before any state is read.</item>
///   <item>Refuses callbacks that carry a dynamic <c>scriptRef</c> in the payload.</item>
///   <item>Verifies the source run is already in HANDED_OFF state before creating Run B.</item>
///   <item>Checks deduplication via <see cref="IRunJournal.HasProcessedCallbackAsync"/>;
///         a duplicate delivery returns an idempotent Ok with
///         <see cref="ReceiveCallbackResult.ContinuationRunCreated"/> = false (AC-3).</item>
///   <item>Creates exactly one Run B per eligible, authenticated, deduplicated callback.</item>
///   <item>Reads the continuation scriptRef exclusively from the static mapping store.</item>
/// </list>
/// </para>
/// No direct third-party API/DB/HTTP calls are made here (CEC invariant #5).
/// </summary>
public sealed class ReceiveCallbackHandler(
    IRunJournal journal,
    IStaticContinuationMappingStore mappingStore,
    ICallbackAuthenticator authenticator,
    IClock clock)
{
    public async Task<Result<ReceiveCallbackResult, Error>> HandleAsync(
        ReceiveCallbackCommand command,
        CancellationToken ct = default)
    {
        // AC-3(auth): Authenticate the inbound callback first — before reading
        // any run state — so that unauthenticated requests are fenced out without
        // leaking information about existing runs.
        // The raw HMAC must NOT be logged (CEC invariant #4).
        var authResult = await authenticator.AuthenticateAsync(
            command.CallbackId, command.CallbackHmac, ct);
        if (authResult.IsFailure)
            return Result<ReceiveCallbackResult, Error>.Fail(authResult.Error);

        // AC-4(a): Refuse any dynamic scriptRef injected by the callback.
        if (!string.IsNullOrWhiteSpace(command.DynamicScriptRefFromCallback))
        {
            return Result<ReceiveCallbackResult, Error>.Fail(Error.Validation(
                "Callback.DynamicScriptRefRefused",
                "The service callback contained a dynamic scriptRef. " +
                "Continuation scriptRefs must come from the static configuration only."));
        }

        // AC-4(b): Verify the source run is in HANDED_OFF state.
        var stateResult = await journal.GetStateAsync(command.SourceRunId, ct);
        if (stateResult.IsFailure)
            return Result<ReceiveCallbackResult, Error>.Fail(stateResult.Error);

        if (stateResult.Value != RunState.HandedOff)
        {
            return Result<ReceiveCallbackResult, Error>.Fail(Error.General(
                "Callback.SourceNotHandedOff",
                $"Cannot create a continuation run because source run '{command.SourceRunId}' " +
                $"is in state '{stateResult.Value}', not HANDED_OFF. " +
                "Continuation runs are created only after HANDED_OFF state is persisted."));
        }

        // AC-3(dedup): Check whether this CallbackId has already been processed.
        // A duplicate delivery returns an idempotent Ok — no second Run B is created.
        var dedupResult = await journal.HasProcessedCallbackAsync(
            command.SourceRunId, command.CallbackId, ct);
        if (dedupResult.IsFailure)
            return Result<ReceiveCallbackResult, Error>.Fail(dedupResult.Error);

        if (dedupResult.Value)
        {
            // Already processed — idempotent 200: return success with no new Run B.
            return Result<ReceiveCallbackResult, Error>.Ok(new ReceiveCallbackResult(
                ContinuationRunCreated: false,
                ContinuationRunId:      null,
                ContinuationScriptRef:  null));
        }

        // AC-4(c): Load the static continuation mapping — scriptRef from config, not callback.
        var mappingResult = await mappingStore.GetMappingAsync(command.ContinuationMappingRef, ct);
        if (mappingResult.IsFailure)
            return Result<ReceiveCallbackResult, Error>.Fail(mappingResult.Error);

        var mapping = mappingResult.Value;

        // AC-3(dedup-persist): Persist the CallbackId before writing Run B so that
        // a crash between the two writes cannot result in a second Run B on retry.
        var markResult = await journal.RecordProcessedCallbackAsync(
            command.SourceRunId, command.CallbackId, ct);
        if (markResult.IsFailure)
            return Result<ReceiveCallbackResult, Error>.Fail(markResult.Error);

        // AC-4(d): Mint a continuation run ID and create exactly one Run B.
        var continuationRunId = Guid.NewGuid().ToString("D");
        var continuationEntry = new RunJournalEntry(
            RunId:            continuationRunId,
            AttemptId:        Guid.NewGuid().ToString("D"),
            LeaseId:          string.Empty,         // No lease yet — queued for pick-up.
            WorkerId:         string.Empty,
            HeartbeatSeq:     0L,
            CheckpointAt:     clock.UtcNow,
            FinalResult:      null,
            IdempotencyClass: IdempotencyClass.Idempotent,
            State:            RunState.Queued);

        var writeResult = await journal.WriteEntryAsync(continuationEntry, ct);
        if (writeResult.IsFailure)
            return Result<ReceiveCallbackResult, Error>.Fail(writeResult.Error);

        return Result<ReceiveCallbackResult, Error>.Ok(new ReceiveCallbackResult(
            ContinuationRunCreated: true,
            ContinuationRunId:      continuationRunId,
            ContinuationScriptRef:  mapping.ScriptRef));
    }
}
