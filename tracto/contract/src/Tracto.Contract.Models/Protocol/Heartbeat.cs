#nullable enable

using System;
using System.Text.Json.Serialization;

namespace Tracto.Contract.Models.Protocol;

/// <summary>Sent periodically by the worker to renew its lease and report current execution status.</summary>
/// <param name="LeaseId">The lease identifier being renewed.</param>
/// <param name="WorkerId">Stable unique identity of the worker sending the heartbeat.</param>
/// <param name="SequenceNumber">Monotonically increasing counter for this lease's heartbeat sequence; starts at 0.</param>
/// <param name="Timestamp">ISO 8601 UTC timestamp at which this heartbeat was sent.</param>
/// <param name="CurrentStepId">Identifier of the step currently executing, or null if between steps.</param>
/// <param name="CurrentRunState">The run state as known by the worker at the time of this heartbeat.</param>
public record Heartbeat(
    [property: JsonPropertyName("leaseId")] string LeaseId,
    [property: JsonPropertyName("workerId")] string WorkerId,
    [property: JsonPropertyName("sequenceNumber")] int SequenceNumber,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("currentStepId")] string? CurrentStepId,
    [property: JsonPropertyName("currentRunState")] RunState CurrentRunState)
{
    /// <summary>Discriminator value identifying this message type.</summary>
    [JsonPropertyName("messageType")]
    public string MessageType => "Heartbeat";
}
