#nullable enable

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Tracto.Contract.Models.Envelope;

public record TargetMap(
    [property: JsonPropertyName("targets")] IReadOnlyDictionary<string, TargetDefinition> Targets);

public record TargetDefinition(
    [property: JsonPropertyName("selectorRef")] string SelectorRef,
    [property: JsonPropertyName("selectorType")] string SelectorType,
    [property: JsonPropertyName("value")] string Value);
