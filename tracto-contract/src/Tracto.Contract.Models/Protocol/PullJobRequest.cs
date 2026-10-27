#nullable enable

using System;
using System.Text.Json.Serialization;

namespace Tracto.Contract.Models.Protocol;

/// <summary>Sent by a worker to request the next available job from the orchestrator.</summary>
/// <param name="WorkerId">Stable unique identity of the requesting worker.</param>
/// <param name="LastSeenAttemptId">The attemptId of the last job the worker saw; null on first pull.</param>
/// <param name="RequestedAt">ISO 8601 UTC timestamp at which the worker sent this request.</param>
public record PullJobRequest(
    [property: JsonPropertyName("workerId")] string WorkerId,
    [property: JsonPropertyName("lastSeenAttemptId")] string? LastSeenAttemptId,
    [property: JsonPropertyName("requestedAt")] DateTimeOffset RequestedAt)
{
    /// <summary>Discriminator value identifying this message type.</summary>
    [JsonPropertyName("messageType")]
    public string MessageType => "PullJobRequest";
}
