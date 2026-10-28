using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Service;

/// <summary>
/// CEC-005: HTTP-based implementation of <see cref="IServiceInvoker"/>.
///
/// <para>Calls the registered independent custom business service identified by
/// <see cref="ServiceInvokeRef.ServiceId"/> via an HTTP POST to its configured endpoint.
/// The response must carry a <c>serviceOutcome</c> field that is one of the five exact
/// CEC v1.0.1 values; any other value is treated as <see cref="ServiceOutcome.UNKNOWN_OUTCOME"/>
/// so the run engine terminates the attempt as UNKNOWN rather than proceeding on an
/// unrecognised outcome.</para>
///
/// <para><b>Security (BRE-006):</b> Input values are forwarded as opaque JSON — no runtime
/// parsing or domain decision is made here.  Secret values must be resolved by the caller
/// before this invoker is called; this class never resolves secret references.</para>
///
/// <para><b>Retry policy (BRE-008):</b> No automatic retry.  Effectful service invocations
/// are never blindly replayed because the business effect may already have been committed
/// before a network failure.  A <see cref="ServiceOutcome.TRANSIENT_FAILURE"/> response from
/// the service is returned as-is; the run engine decides whether reconciliation allows a new
/// attempt on a future run.</para>
/// </summary>
public sealed class ServiceInvoker(
    HttpClient httpClient,
    IOptions<ServiceInvokerOptions> options,
    ILogger<ServiceInvoker> logger)
    : IServiceInvoker
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    /// <inheritdoc/>
    public async Task<ServiceInvokeResult> InvokeAsync(
        ServiceInvokeRef serviceRef,
        Dictionary<string, object?> input,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(serviceRef);

        // ── Resolve registered endpoint ─────────────────────────────────────────
        if (!options.Value.Endpoints.TryGetValue(serviceRef.ServiceId, out var endpoint))
        {
            logger.LogError(
                "ServiceInvoker: no registered endpoint for serviceId='{ServiceId}'. " +
                "Only services declared in ServiceInvokerOptions.Endpoints are permitted.",
                serviceRef.ServiceId);

            // Treat unregistered service as a TECHNICAL_FAILURE — the run engine will
            // terminate the attempt as FAILED; a new attempt must use a valid serviceId.
            return new ServiceInvokeResult
            {
                Outcome    = ServiceOutcome.TECHNICAL_FAILURE,
                ActionCode = null
            };
        }

        var timeout = endpoint.Timeout ?? options.Value.DefaultTimeout;

        logger.LogInformation(
            "ServiceInvoker: invoking serviceId='{ServiceId}' at '{Endpoint}' (timeout={Timeout}s).",
            serviceRef.ServiceId, endpoint.BaseAddress, timeout.TotalSeconds);

        // ── Build request payload ───────────────────────────────────────────────
        var payload = new ServiceInvokePayload
        {
            ServiceId    = serviceRef.ServiceId,
            InputMapping = serviceRef.InputMapping,
            Input        = input
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        // ── HTTP POST — no retry per BRE-008 ───────────────────────────────────
        HttpResponseMessage response;
        try
        {
            response = await httpClient
                .PostAsJsonAsync(endpoint.BaseAddress, payload, _jsonOpts, cts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Timeout from our own CTS, not from the run's cancellation token.
            logger.LogWarning(
                "ServiceInvoker: HTTP call to serviceId='{ServiceId}' timed out after {Timeout}s. " +
                "Returning UNKNOWN_OUTCOME — attempt will terminate as UNKNOWN.",
                serviceRef.ServiceId, timeout.TotalSeconds);

            // Timed-out HTTP call is an uncertain outcome — the service may have received and
            // committed the request before the timeout fired (BRE-003 / BRW-US-015 AC#2).
            return new ServiceInvokeResult
            {
                Outcome    = ServiceOutcome.UNKNOWN_OUTCOME,
                ActionCode = null
            };
        }
        catch (HttpRequestException hex)
        {
            // Network/transport failure — outcome is uncertain; terminate as UNKNOWN.
            logger.LogWarning(hex,
                "ServiceInvoker: network error invoking serviceId='{ServiceId}'. " +
                "Returning UNKNOWN_OUTCOME — attempt will terminate as UNKNOWN.",
                serviceRef.ServiceId);

            return new ServiceInvokeResult
            {
                Outcome    = ServiceOutcome.UNKNOWN_OUTCOME,
                ActionCode = null
            };
        }

        // ── Parse response ──────────────────────────────────────────────────────
        ServiceInvokeResponse? svcResponse = null;
        try
        {
            svcResponse = await response.Content
                .ReadFromJsonAsync<ServiceInvokeResponse>(_jsonOpts, ct)
                .ConfigureAwait(false);
        }
        catch (JsonException jex)
        {
            logger.LogError(jex,
                "ServiceInvoker: failed to parse JSON response from serviceId='{ServiceId}'. " +
                "HTTP status={Status}. Returning TECHNICAL_FAILURE.",
                serviceRef.ServiceId, (int)response.StatusCode);

            return new ServiceInvokeResult
            {
                Outcome    = ServiceOutcome.TECHNICAL_FAILURE,
                ActionCode = null
            };
        }

        if (svcResponse is null)
        {
            logger.LogError(
                "ServiceInvoker: empty response body from serviceId='{ServiceId}'. " +
                "HTTP status={Status}. Returning TECHNICAL_FAILURE.",
                serviceRef.ServiceId, (int)response.StatusCode);

            return new ServiceInvokeResult
            {
                Outcome    = ServiceOutcome.TECHNICAL_FAILURE,
                ActionCode = null
            };
        }

        // ── Validate outcome enum ──────────────────────────────────────────────
        // If the service returns a string that is not one of the five exact CEC values,
        // treat it as UNKNOWN_OUTCOME rather than throwing — keeps the run engine in
        // control of the terminal state decision.
        if (!Enum.TryParse<ServiceOutcome>(svcResponse.ServiceOutcome, ignoreCase: true, out var outcome))
        {
            logger.LogError(
                "ServiceInvoker: serviceId='{ServiceId}' returned unrecognised serviceOutcome='{Value}'. " +
                "Treating as UNKNOWN_OUTCOME — attempt will terminate as UNKNOWN.",
                serviceRef.ServiceId, svcResponse.ServiceOutcome ?? "(null)");

            return new ServiceInvokeResult
            {
                Outcome    = ServiceOutcome.UNKNOWN_OUTCOME,
                ActionCode = null
            };
        }

        logger.LogInformation(
            "ServiceInvoker: serviceId='{ServiceId}' responded with serviceOutcome={Outcome}, " +
            "actionCode={ActionCode}.",
            serviceRef.ServiceId, outcome, svcResponse.ActionCode ?? "(none)");

        return new ServiceInvokeResult
        {
            Outcome    = outcome,
            ActionCode = svcResponse.ActionCode,
            Output     = svcResponse.Output ?? []
        };
    }

    // ── Internal wire types ────────────────────────────────────────────────────

    private sealed record ServiceInvokePayload
    {
        public required string ServiceId { get; init; }
        public Dictionary<string, object?> InputMapping { get; init; } = [];
        public Dictionary<string, object?> Input { get; init; } = [];
    }

    private sealed record ServiceInvokeResponse
    {
        /// <summary>
        /// One of the five CEC v1.0.1 service outcome values as a string:
        /// SUCCESS, BUSINESS_REJECTION, TECHNICAL_FAILURE, TRANSIENT_FAILURE, UNKNOWN_OUTCOME.
        /// </summary>
        public string? ServiceOutcome { get; init; }

        /// <summary>
        /// Optional action code key for the post-service UI step.
        /// Used as a lookup key into the statically configured action map; never
        /// interpreted as a domain value or business decision by the runtime.
        /// </summary>
        public string? ActionCode { get; init; }

        /// <summary>
        /// Opaque output values forwarded from the service.
        /// The runtime does not parse or interpret these — they are passed through
        /// to downstream steps as opaque data.
        /// </summary>
        public Dictionary<string, object?>? Output { get; init; }
    }
}
