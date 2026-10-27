#nullable enable

using System.Text.Json.Serialization;

namespace Tracto.Contract.Models;

[JsonConverter(typeof(JsonStringEnumConverter<RunState>))]
public enum RunState
{
    [JsonPropertyName("CREATED")]
    Created,

    [JsonPropertyName("QUEUED")]
    Queued,

    [JsonPropertyName("LEASED")]
    Leased,

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

    [JsonPropertyName("CANCELLED")]
    Cancelled,

    [JsonPropertyName("HANDED_OFF")]
    HandedOff,
}
