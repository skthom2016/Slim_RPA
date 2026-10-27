#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tracto.Contract.Models.Envelope;

/// <summary>Script-scoped variable declaration (C1 decision).</summary>
public record VarDeclaration(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("nullable")] bool? Nullable = null,
    [property: JsonPropertyName("defaultValue")] JsonElement? DefaultValue = null,
    [property: JsonPropertyName("sensitivity")] string? Sensitivity = null);
