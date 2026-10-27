#nullable enable

using System.Text.Json.Serialization;

namespace Tracto.Contract.Models;

[JsonConverter(typeof(JsonStringEnumConverter<EvidenceClassification>))]
public enum EvidenceClassification
{
    [JsonPropertyName("TECHNICAL_METADATA")]
    TechnicalMetadata,

    [JsonPropertyName("BUSINESS_REFERENCE")]
    BusinessReference,

    [JsonPropertyName("UI_ASSERTION")]
    UiAssertion,

    [JsonPropertyName("SANITIZED_EVIDENCE")]
    SanitizedEvidence,
}
