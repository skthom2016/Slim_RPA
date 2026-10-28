#nullable enable

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Tracto.Contract.Models.Telemetry;

public record EvidenceManifest(
    [property: JsonPropertyName("runId")] string RunId,
    [property: JsonPropertyName("attemptId")] string AttemptId,
    [property: JsonPropertyName("entries")] IReadOnlyList<EvidenceEntry> Entries);
