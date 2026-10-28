using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.Service;
using Tracto.TerminalRuntime.Domain.Service;

namespace Tracto.TerminalRuntime.Infrastructure.Service;

/// <summary>
/// Posts a business call to the <c>service.invoke</c> endpoint and deserialises
/// the <see cref="ServiceInvokeResult"/>.
///
/// CEC v1.0.1 invariants enforced here:
/// <list type="bullet">
///   <item>#4: No plaintext secrets are included in request or response logs.</item>
///   <item>#5: This is the ONLY approved path for outbound business calls; no direct
///             third-party API, DB, or raw HTTP calls may bypass this client.</item>
///   <item>#6: The five <see cref="ServiceOutcome"/> values are the only recognised
///             categories; unrecognised values surface as a Result failure.</item>
/// </list>
/// </summary>
public sealed class ServiceInvokeClient(
    HttpClient httpClient,
    ILogger<ServiceInvokeClient> logger) : IServiceInvokeClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    // ── IServiceInvokeClient ──────────────────────────────────────────────────

    public async Task<Result<ServiceInvokeResult, Error>> InvokeAsync(
        string serviceRef,
        object? payload,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(serviceRef))
            return Result<ServiceInvokeResult, Error>.Fail(Error.Validation(
                "ServiceInvoke.ServiceRefRequired",
                "serviceRef must not be null or whitespace."));

        try
        {
            // Log the service reference only — never the payload content to avoid
            // accidentally leaking secrets (CEC invariant #4).
            logger.LogDebug(
                "Invoking service endpoint for serviceRef=[redacted] — payload omitted from log (CEC invariant #4).");

            var requestUri = BuildUri(serviceRef);

            using var response = await httpClient
                .PostAsJsonAsync(requestUri, payload, SerializerOptions, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "service.invoke returned non-success status {Status}.", (int)response.StatusCode);

                return Result<ServiceInvokeResult, Error>.Fail(Error.General(
                    "ServiceInvoke.HttpError",
                    $"service.invoke returned HTTP {(int)response.StatusCode}."));
            }

            var wire = await response.Content
                .ReadFromJsonAsync<ServiceInvokeWireResponse>(SerializerOptions, ct)
                .ConfigureAwait(false);

            if (wire is null)
                return Result<ServiceInvokeResult, Error>.Fail(Error.General(
                    "ServiceInvoke.EmptyResponse",
                    "service.invoke returned an empty or unparseable response body."));

            if (!Enum.TryParse<ServiceOutcome>(wire.ServiceOutcome, ignoreCase: true, out var outcome))
                return Result<ServiceInvokeResult, Error>.Fail(Error.General(
                    "ServiceInvoke.UnrecognisedOutcome",
                    $"Unrecognised serviceOutcome value. Expected one of: " +
                    $"{string.Join(", ", Enum.GetNames<ServiceOutcome>())}."));

            logger.LogInformation(
                "service.invoke responded with outcome {Outcome}.", outcome);

            return Result<ServiceInvokeResult, Error>.Ok(new ServiceInvokeResult(
                outcome,
                wire.ActionCode,
                wire.BusinessOutcome));
        }
        catch (OperationCanceledException)
        {
            return Result<ServiceInvokeResult, Error>.Fail(Error.Timeout(
                "ServiceInvoke.Cancelled",
                "The service.invoke call was cancelled or timed out."));
        }
        catch (HttpRequestException hrex)
        {
            logger.LogWarning(hrex, "service.invoke HTTP transport error.");
            return Result<ServiceInvokeResult, Error>.Fail(Error.General(
                "ServiceInvoke.TransportError",
                "A transport error occurred while calling service.invoke."));
        }
        catch (JsonException jex)
        {
            logger.LogWarning(jex, "service.invoke response could not be deserialised.");
            return Result<ServiceInvokeResult, Error>.Fail(Error.General(
                "ServiceInvoke.DeserialiseError",
                "The service.invoke response body could not be deserialised."));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during service.invoke.");
            return Result<ServiceInvokeResult, Error>.Fail(Error.General(
                "ServiceInvoke.Unexpected",
                "An unexpected error occurred during service.invoke."));
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildUri(string serviceRef) =>
        $"service.invoke/{Uri.EscapeDataString(serviceRef)}";

    // ── Wire DTO ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal wire representation of the service.invoke HTTP response body.
    /// Only the approved structural fields are deserialised; free-form business
    /// fields are deliberately excluded to prevent domain-logic leakage.
    /// </summary>
    private sealed record ServiceInvokeWireResponse(
        string  ServiceOutcome,
        string? ActionCode,
        string? BusinessOutcome);
}
