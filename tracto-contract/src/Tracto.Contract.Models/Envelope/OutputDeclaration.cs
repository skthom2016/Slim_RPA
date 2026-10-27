#nullable enable

using System.Text.Json.Serialization;
using Tracto.Contract.Models.Protocol;

namespace Tracto.Contract.Models.Envelope;

public record OutputDeclaration(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("evidenceClassification")] EvidenceClassification EvidenceClassification,
    [property: JsonPropertyName("nullable")] bool? Nullable = null);
