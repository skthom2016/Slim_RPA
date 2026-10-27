#nullable enable

using System;
using System.Text.Json.Serialization;

namespace Tracto.Contract.Models.Protocol;

/// <summary>Sent by the worker to confirm receipt and acceptance of a LeaseGranted message.</summary>
/// <param name="LeaseId">The lease identifier from the corresponding LeaseGranted message.</param>
/// <param name="WorkerId">Stable unique identity of the worker accepting the lease.</param>
/// <param name="AcceptedAt">ISO 8601 UTC timestamp at which the worker accepted the lease.</param>
public record LeaseAccepted(
    [property: JsonPropertyName("leaseId")] string LeaseId,
    [property: JsonPropertyName("workerId")] string WorkerId,
    [property: JsonPropertyName("acceptedAt")] DateTimeOffset AcceptedAt)
{
    /// <summary>Discriminator value identifying this message type.</summary>
    [JsonPropertyName("messageType")]
    public string MessageType => "LeaseAccepted";
}
