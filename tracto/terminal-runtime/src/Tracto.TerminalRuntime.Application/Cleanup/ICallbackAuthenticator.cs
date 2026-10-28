using Tracto.TerminalRuntime.Application.Common;

namespace Tracto.TerminalRuntime.Application.Cleanup;

/// <summary>
/// Verifies that an inbound async service callback is authentic before a
/// continuation run (Run B) is created.
///
/// Implementations must NOT log, persist, or forward the raw credential in any
/// form (CEC v1.0.1 invariant #4 — no plaintext secrets in logs or payloads).
///
/// TER-US-019 AC-3: the callback must be "eligible, authenticated, deduplicated".
/// </summary>
public interface ICallbackAuthenticator
{
    /// <summary>
    /// Authenticates the inbound callback.
    /// </summary>
    /// <param name="callbackId">
    ///   Idempotency key for this callback delivery — used as additional context
    ///   for HMAC verification (bound to the specific delivery, not reusable).
    /// </param>
    /// <param name="callbackHmac">
    ///   HMAC-SHA256 (or equivalent) credential supplied by the remote service.
    ///   The raw value must never be written to logs or persisted (CEC invariant #4).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///   <see cref="Result{T,E}.Ok"/> when authentication succeeds, or
    ///   <see cref="Error.Authentication"/> when it fails.
    /// </returns>
    Task<Result<bool, Error>> AuthenticateAsync(
        string callbackId,
        string callbackHmac,
        CancellationToken ct = default);
}
