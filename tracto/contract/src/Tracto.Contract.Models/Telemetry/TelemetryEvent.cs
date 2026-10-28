#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tracto.Contract.Models.Execution;

namespace Tracto.Contract.Models.Telemetry;

public record TelemetryEvent(
    [property: JsonPropertyName("eventId")] string EventId,
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    [property: JsonPropertyName("eventType")] TelemetryEventType EventType,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("businessJobId")] string? BusinessJobId = null,
    [property: JsonPropertyName("leaseId")] string? LeaseId = null,
    [property: JsonPropertyName("runId")] string? RunId = null,
    [property: JsonPropertyName("attemptId")] string? AttemptId = null,
    [property: JsonPropertyName("stepId")] string? StepId = null,
    [property: JsonPropertyName("workerId")] string? WorkerId = null,
    [property: JsonPropertyName("runState")] RunState? RunState = null,
    [property: JsonPropertyName("stepState")] StepState? StepState = null,
    [property: JsonPropertyName("errorCategory")] ErrorCategory? ErrorCategory = null,
    [property: JsonPropertyName("errorDetail")] string? ErrorDetail = null,
    [property: JsonPropertyName("durationMs")] long? DurationMs = null,
    [property: JsonPropertyName("metadata")] IReadOnlyDictionary<string, JsonElement>? Metadata = null);
