#nullable enable

using System.Text.Json.Serialization;

namespace Tracto.Contract.Models;

[JsonConverter(typeof(JsonStringEnumConverter<HostClass>))]
public enum HostClass
{
    [JsonPropertyName("interactive")]
    Interactive,

    [JsonPropertyName("headless")]
    Headless,
}
