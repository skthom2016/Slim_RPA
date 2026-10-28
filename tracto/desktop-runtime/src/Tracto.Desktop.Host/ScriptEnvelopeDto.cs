#nullable enable
using System.Text.Json.Serialization;

namespace Tracto.Desktop.Host;

/// <summary>Response from GET /artifacts/{id}/versions/{version}.</summary>
internal sealed record ArtifactGetResponse(
    [property: JsonPropertyName("payload")]     string Payload,
    [property: JsonPropertyName("contentHash")] string ContentHash);

/// <summary>Top-level envelope as written by LocalFilePublisher.</summary>
internal sealed record ScriptEnvelope(
    [property: JsonPropertyName("publicationId")] string? PublicationId,
    [property: JsonPropertyName("script")]         EnvelopeScript Script);

internal sealed record EnvelopeScript(
    [property: JsonPropertyName("Id")]      string Id,
    [property: JsonPropertyName("Name")]    string Name,
    [property: JsonPropertyName("Version")] string Version,
    [property: JsonPropertyName("steps")]   IReadOnlyList<EnvelopeStep> Steps);

/// <summary>
/// One recorded step. Parameters is Dictionary&lt;string, string?&gt; matching ScriptStep.Parameters.
/// </summary>
internal sealed record EnvelopeStep(
    [property: JsonPropertyName("StepNumber")]  int StepNumber,
    [property: JsonPropertyName("Action")]      string Action,
    [property: JsonPropertyName("Description")] string? Description,
    [property: JsonPropertyName("Parameters")]  IReadOnlyDictionary<string, string?>? Parameters);
