#nullable enable

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tracto.Contract.Models.Protocol;

namespace Tracto.Contract.Models.Envelope;

public record StepDefinition(
    [property: JsonPropertyName("stepId")] string StepId,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("inputs")] IReadOnlyDictionary<string, JsonElement>? Inputs = null,
    [property: JsonPropertyName("outputBinding")] IReadOnlyDictionary<string, string>? OutputBinding = null,
    [property: JsonPropertyName("timeoutMs")] int? TimeoutMs = null,
    [property: JsonPropertyName("retryClass")] RetryClass? RetryClass = null,
    [property: JsonPropertyName("precondition")] JsonElement? Precondition = null,
    [property: JsonPropertyName("postcondition")] JsonElement? Postcondition = null,
    [property: JsonPropertyName("onFailure")] OnFailureConfig? OnFailure = null,
    [property: JsonPropertyName("sensitivity")] string? Sensitivity = null);
