#nullable enable

using System.Text.Json.Serialization;

namespace Tracto.Contract.Models.Protocol;

/// <summary>Sent by the orchestrator when no suitable job is currently available for the requesting worker.</summary>
/// <param name="RetryAfterMs">Milliseconds the worker should wait before sending another PullJobRequest.</param>
public record NoJobAvailable(
    [property: JsonPropertyName("retryAfterMs")] int RetryAfterMs)
{
    /// <summary>Discriminator value identifying this message type.</summary>
    [JsonPropertyName("messageType")]
    public string MessageType => "NoJobAvailable";
}
