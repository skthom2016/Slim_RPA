#nullable enable

using System.Text.Json.Serialization;

namespace Tracto.Contract.Models;

[JsonConverter(typeof(JsonStringEnumConverter<WorkerHealthStatus>))]
public enum WorkerHealthStatus
{
    [JsonPropertyName("READY")]
    Ready,

    [JsonPropertyName("DEGRADED")]
    Degraded,

    [JsonPropertyName("UNAVAILABLE")]
    Unavailable,
}
