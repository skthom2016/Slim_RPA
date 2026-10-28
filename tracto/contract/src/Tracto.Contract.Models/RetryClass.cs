#nullable enable

using System.Text.Json.Serialization;

namespace Tracto.Contract.Models;

[JsonConverter(typeof(JsonStringEnumConverter<RetryClass>))]
public enum RetryClass
{
    [JsonPropertyName("TRANSIENT_SAFE")]
    TransientSafe,

    [JsonPropertyName("IDEMPOTENT")]
    Idempotent,

    [JsonPropertyName("ONCE_ONLY")]
    OnceOnly,

    [JsonPropertyName("NEVER")]
    Never,
}
