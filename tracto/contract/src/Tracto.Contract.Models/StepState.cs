#nullable enable

using System.Text.Json.Serialization;

namespace Tracto.Contract.Models;

[JsonConverter(typeof(JsonStringEnumConverter<StepState>))]
public enum StepState
{
    [JsonPropertyName("PENDING")]
    Pending,

    [JsonPropertyName("RUNNING")]
    Running,

    [JsonPropertyName("SUCCEEDED")]
    Succeeded,

    [JsonPropertyName("FAILED")]
    Failed,

    [JsonPropertyName("TIMED_OUT")]
    TimedOut,

    [JsonPropertyName("UNKNOWN")]
    Unknown,

    [JsonPropertyName("SKIPPED")]
    Skipped,

    [JsonPropertyName("CANCELLED")]
    Cancelled,
}
