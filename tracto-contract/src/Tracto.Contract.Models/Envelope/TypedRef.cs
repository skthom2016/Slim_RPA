#nullable enable

using System.Text.Json.Serialization;

namespace Tracto.Contract.Models.Envelope;

/// <summary>Reference pattern: {"ref":"inputs.x"}.</summary>
public record TypedRef([property: JsonPropertyName("ref")] string Ref);
