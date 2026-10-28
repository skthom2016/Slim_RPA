using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Service;

namespace Tracto.TerminalRuntime.Application.Service;

/// <summary>
/// Contract for posting a business call to the <c>service.invoke</c> endpoint and
/// deserialising the <see cref="ServiceInvokeResult"/>.
///
/// CEC v1.0.1 invariant #5: service.invoke is the ONLY approved path for business
/// calls from terminal commands.  No direct third-party API, DB, or HTTP calls may
/// bypass this interface.
///
/// CEC v1.0.1 invariant #4: no plaintext secrets may appear in <paramref name="serviceRef"/>
/// or in any serialised payload field.
/// </summary>
public interface IServiceInvokeClient
{
    /// <summary>
    /// Posts the serialised <paramref name="payload"/> to the service identified by
    /// <paramref name="serviceRef"/> and returns the typed result.
    /// </summary>
    /// <param name="serviceRef">
    ///   The approved service reference registered in the command template.
    ///   Must not be null or whitespace, and must not embed plaintext credentials.
    /// </param>
    /// <param name="payload">
    ///   The request body to send.  Must not contain plaintext secrets.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<Result<ServiceInvokeResult, Error>> InvokeAsync(
        string serviceRef,
        object? payload,
        CancellationToken ct = default);
}
