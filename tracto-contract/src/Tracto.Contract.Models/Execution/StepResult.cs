#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;
using Tracto.Contract.Models.Execution;

namespace Tracto.Contract.Models.Execution;

public record StepResult(
    [property: JsonPropertyName("stepId")] string StepId,
    [property: JsonPropertyName("stepState")] StepState StepState,
    [property: JsonPropertyName("startedAt")] DateTimeOffset StartedAt,
    [property: JsonPropertyName("completedAt")] DateTimeOffset CompletedAt,
    [property: JsonPropertyName("durationMs")] long DurationMs,
    [property: JsonPropertyName("outputs")] IReadOnlyDictionary<string, JsonElement>? Outputs = null,
    [property: JsonPropertyName("errorCategory")] ErrorCategory? ErrorCategory = null,
    [property: JsonPropertyName("errorDetail")] string? ErrorDetail = null,
    [property: JsonPropertyName("needsAttention")] bool? NeedsAttention = null,
    [property: JsonPropertyName("unknownReason")] string? UnknownReason = null);
