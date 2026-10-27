#nullable enable

using System.Text.Json.Serialization;
using Tracto.Contract.Models.Execution;

namespace Tracto.Contract.Models.Execution;

public record EvidenceRef(
    [property: JsonPropertyName("evidenceRef")] string EvidenceRefValue,
    [property: JsonPropertyName("classification")] EvidenceClassification Classification,
    [property: JsonPropertyName("stepId")] string? StepId = null);
