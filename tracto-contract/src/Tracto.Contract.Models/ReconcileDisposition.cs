#nullable enable

using System.Text.Json.Serialization;

namespace Tracto.Contract.Models;

[JsonConverter(typeof(JsonStringEnumConverter<ReconcileDisposition>))]
public enum ReconcileDisposition
{
    [JsonPropertyName("SAFE_TO_REQUEUE")]
    SafeToRequeue,

    [JsonPropertyName("UNKNOWN")]
    Unknown,

    [JsonPropertyName("ALREADY_TERMINAL")]
    AlreadyTerminal,
}
