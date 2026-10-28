#nullable enable

// Approved output evidence/telemetry classification vocabulary (REF-08 confirmed).
// Every output declaration must carry one of these classifications.
// This enforces the disclosure prohibition: business data content may not flow through telemetry.

using System.Text.Json.Serialization;

namespace Tracto.Contract.Execution;

[JsonConverter(typeof(JsonStringEnumConverter<EvidenceClassification>))]
public enum EvidenceClassification
{
    // Technical runtime metadata (timing, step IDs, state transitions).
    [JsonPropertyName("TECHNICAL_METADATA")] TechnicalMetadata,

    // Opaque correlation/reference identifier — not business data content.
    // A service-returned correlation ID passed to a subsequent step is permitted here;
    // passing business payload content is not.
    [JsonPropertyName("BUSINESS_REFERENCE")] BusinessReference,

    // UI state assertion (element text, visibility result, etc.).
    [JsonPropertyName("UI_ASSERTION")] UiAssertion,

    // Business data that has been sanitized/redacted before inclusion.
    [JsonPropertyName("SANITIZED_EVIDENCE")] SanitizedEvidence,
}
