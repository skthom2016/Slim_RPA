#nullable enable

using System.Text.Json.Serialization;

namespace Tracto.Contract.Models.Protocol;

/// <summary>Sent by the orchestrator to acknowledge a Heartbeat and optionally signal cancellation.</summary>
/// <param name="LeaseId">The lease identifier being acknowledged.</param>
/// <param name="SequenceNumber">Echo of the sequenceNumber from the Heartbeat being acknowledged.</param>
/// <param name="CancellationRequested">When true, the orchestrator is requesting the worker cancel the current run gracefully.</param>
public record HeartbeatAck(
    [property: JsonPropertyName("leaseId")] string LeaseId,
    [property: JsonPropertyName("sequenceNumber")] int SequenceNumber,
    [property: JsonPropertyName("cancellationRequested")] bool CancellationRequested)
{
    /// <summary>Discriminator value identifying this message type.</summary>
    [JsonPropertyName("messageType")]
    public string MessageType => "HeartbeatAck";
}
