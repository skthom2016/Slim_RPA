using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Continuation;

/// <summary>
/// CEC-016: Gate for async continuation after a run reaches the HANDED_OFF state.
///
/// <para>
/// The HANDED_OFF run state is set ONLY on a confirmed asynchronous acknowledgement —
/// never speculatively.  A continuation run (Run B) is created by the Orchestrator ONLY
/// when an eligible, authenticated, and deduplicated callback arrives for a source run
/// that is already persisted as HANDED_OFF.  The service that generated the original
/// callback cannot supply a <c>scriptRef</c>; the continuation script is statically
/// configured in the Orchestrator.
/// </para>
/// </summary>
public interface IAsyncContinuationHandler
{
    /// <summary>
    /// Records that <paramref name="runId"/> has been persisted as HANDED_OFF and is
    /// awaiting an eligible callback.
    /// Does NOT create a continuation run — that happens only in
    /// <see cref="ProcessCallbackAsync"/> when an eligible callback arrives.
    /// </summary>
    Task RegisterHandedOffAsync(string runId, string leaseToken, CancellationToken ct);

    /// <summary>
    /// Processes an incoming callback notification.
    /// A continuation run is created ONLY when:
    /// <list type="number">
    ///   <item>The source run is in HANDED_OFF state (registered via <see cref="RegisterHandedOffAsync"/>)</item>
    ///   <item>The callback token has not been seen before (deduplication)</item>
    ///   <item>The callback carries a non-empty <see cref="CallbackNotification.AuthTag"/></item>
    ///   <item>The callback's <see cref="CallbackNotification.ServiceOutcome"/> is not
    ///         <see cref="ServiceOutcome.UNKNOWN_OUTCOME"/> — that terminates as UNKNOWN</item>
    /// </list>
    /// </summary>
    Task<ContinuationResult> ProcessCallbackAsync(CallbackNotification notification, CancellationToken ct);
}
