#nullable enable

using System;
using System.Text.Json.Serialization;

namespace Tracto.Contract.Models.Protocol;

/// <summary>Sent by the orchestrator to request cancellation of an active run.</summary>
/// <param name="LeaseId">The lease identifier of the run to be cancelled.</param>
/// <param name="RequestedAt">ISO 8601 UTC timestamp at which the cancellation was requested.</param>
/// <param name="Reason">Human-readable reason for the cancellation, or null if not provided.</param>
public record CancelRequest(
    [property: JsonPropertyName("leaseId")] string LeaseId,
    [property: JsonPropertyName("requestedAt")] DateTimeOffset RequestedAt,
    [property: JsonPropertyName("reason")] string? Reason)
{
    /// <summary>Discriminator value identifying this message type.</summary>
    [JsonPropertyName("messageType")]
    public string MessageType => "CancelRequest";
}
