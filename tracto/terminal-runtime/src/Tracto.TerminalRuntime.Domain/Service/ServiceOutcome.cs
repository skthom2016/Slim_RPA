namespace Tracto.TerminalRuntime.Domain.Service;

/// <summary>
/// The five exact service-outcome categories returned by a service.invoke call.
///
/// CEC v1.0.1 invariant #6: exactly these five values are recognised; the runtime
/// branches only on this category (or an approved actionCode), never on domain fields
/// such as monetary amounts or business identifiers.
/// </summary>
public enum ServiceOutcome
{
    /// <summary>
    /// The service completed the requested operation successfully.
    /// Maps the run to <see cref="RunState.Succeeded"/>.
    /// </summary>
    Succeeded,

    /// <summary>
    /// The service encountered a technical failure it could not recover from.
    /// Maps the run to <see cref="RunState.Failed"/>.
    /// </summary>
    Failed,

    /// <summary>
    /// The service rejected the request for a valid business reason (e.g. insufficient
    /// funds, invalid account, duplicate order).  This is NOT a technical error.
    /// Maps the run to <see cref="RunState.Succeeded"/> with
    /// <see cref="ServiceInvokeResult.BusinessOutcome"/> set to "REJECTED".
    /// No retry is triggered.
    /// </summary>
    BusinessRejection,

    /// <summary>
    /// The service's side-effect is indeterminate — the service could not confirm
    /// whether the operation was applied.
    /// Maps the attempt to <see cref="RunState.Unknown"/> with
    /// <c>needsAttention=true</c> (CEC v1.0.1 invariant #2).
    /// No continuation is allowed until external reconciliation confirms the outcome.
    /// </summary>
    UnknownOutcome,

    /// <summary>
    /// The service has accepted the request and will complete it asynchronously.
    /// Maps the run to <see cref="RunState.HandedOff"/>, releasing the Terminal worker
    /// immediately.  A continuation run is created only when an eligible, authenticated,
    /// deduplicated callback arrives.
    /// </summary>
    AsyncAcknowledged,
}
