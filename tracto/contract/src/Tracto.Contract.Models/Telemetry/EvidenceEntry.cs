#nullable enable

using System;
using System.Text.Json.Serialization;

namespace Tracto.Contract.Models.Telemetry;

public record EvidenceEntry(
    [property: JsonPropertyName("evidenceRef")] string EvidenceRef,
    [property: JsonPropertyName("classification")] EvidenceClassification Classification,
    [property: JsonPropertyName("stepId")] string? StepId,
    [property: JsonPropertyName("retentionPolicy")] string? RetentionPolicy,
    [property: JsonPropertyName("recordedAt")] DateTimeOffset RecordedAt);
