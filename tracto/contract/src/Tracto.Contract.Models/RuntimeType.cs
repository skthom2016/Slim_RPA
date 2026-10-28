#nullable enable

using System.Text.Json.Serialization;

namespace Tracto.Contract.Models;

[JsonConverter(typeof(JsonStringEnumConverter<RuntimeType>))]
public enum RuntimeType
{
    [JsonPropertyName("desktop")]
    Desktop,

    [JsonPropertyName("browser")]
    Browser,

    [JsonPropertyName("terminal")]
    Terminal,
}
