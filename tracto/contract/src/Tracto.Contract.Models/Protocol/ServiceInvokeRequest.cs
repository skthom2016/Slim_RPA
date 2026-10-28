#nullable enable

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tracto.Contract.Models.Protocol;

/// <summary>Parameters for invoking a service operation from a service.invoke step.</summary>
/// <param name="ServiceRef">Stable logical identifier for the target service.</param>
/// <param name="OperationRef">Identifies the operation to invoke on the target service.</param>
/// <param name="Inputs">Key-value map of inputs passed to the service operation.</param>
/// <param name="TimeoutMs">Maximum milliseconds to wait for the service operation to complete.</param>
/// <param name="RetryClass">Declares the retry semantics for this invocation.</param>
/// <param name="IdempotencyKey">Unique key used by the service to deduplicate repeated invocations.</param>
/// <param name="CallbackConfig">Configuration for async callback delivery; required when the operation does not return a synchronous response.</param>
public record ServiceInvokeRequest(
    [property: JsonPropertyName("serviceRef")] string ServiceRef,
    [property: JsonPropertyName("operationRef")] string OperationRef,
    [property: JsonPropertyName("inputs")] IReadOnlyDictionary<string, JsonElement>? Inputs = null,
    [property: JsonPropertyName("timeoutMs")] int? TimeoutMs = null,
    [property: JsonPropertyName("retryClass")] RetryClass? RetryClass = null,
    [property: JsonPropertyName("idempotencyKey")] string? IdempotencyKey = null,
    [property: JsonPropertyName("callbackConfig")] CallbackConfig? CallbackConfig = null);
