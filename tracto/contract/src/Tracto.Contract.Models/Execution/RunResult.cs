#nullable enable

using System.Text.Json.Serialization;
using Tracto.Contract.Models.Execution;

namespace Tracto.Contract.Models.Execution;

public record RunResult(
    [property: JsonPropertyName("runId")] string RunId,
    [property: JsonPropertyName("attemptId")] string AttemptId,
    [property: JsonPropertyName("leaseId")] string LeaseId,
    [property: JsonPropertyName("workerId")] string WorkerId,
    [property: JsonPropertyName("runState")] RunState RunState,
    [property: JsonPropertyName("startedAt")] DateTimeOffset StartedAt,
    [property: JsonPropertyName("completedAt")] DateTimeOffset CompletedAt,
    [property: JsonPropertyName("durationMs")] long DurationMs,
    [property: JsonPropertyName("lastHeartbeatAt")] DateTimeOffset? LastHeartbeatAt,
    [property: JsonPropertyName("stepResults")] IReadOnlyList<StepResult> StepResults,
    [property: JsonPropertyName("errorCategory")] ErrorCategory? ErrorCategory = null,
    [property: JsonPropertyName("errorDetail")] string? ErrorDetail = null,
    [property: JsonPropertyName("needsAttention")] bool? NeedsAttention = null,
    [property: JsonPropertyName("unknownReason")] string? UnknownReason = null,
    [property: JsonPropertyName("evidenceRefs")] IReadOnlyList<EvidenceRef>? EvidenceRefs = null,
    [property: JsonPropertyName("continuationTriggered")] bool? ContinuationTriggered = null);
