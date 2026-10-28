#nullable enable

using System.Text.Json.Serialization;

namespace Tracto.Contract.Models.Envelope;

public record InputDeclaration(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("nullable")] bool? Nullable = null,
    [property: JsonPropertyName("sensitive")] bool? Sensitive = null);
