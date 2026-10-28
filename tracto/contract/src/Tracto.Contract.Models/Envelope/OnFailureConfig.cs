#nullable enable

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Tracto.Contract.Models.Envelope;

public record OnFailureConfig(
    [property: JsonPropertyName("handler")] string Handler,
    [property: JsonPropertyName("compensatingSteps")] IReadOnlyList<string>? CompensatingSteps = null);
