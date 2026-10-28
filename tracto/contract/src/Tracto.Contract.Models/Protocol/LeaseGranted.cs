#nullable enable

using System.Text.Json.Serialization;

namespace Tracto.Contract.Models.Protocol;

/// <summary>Sent by the orchestrator to assign a job lease to the requesting worker.</summary>
/// <param name="LeaseId">Unique identifier for this lease.</param>
/// <param name="RunId">Identifier of the run this lease is for.</param>
/// <param name="AttemptId">Identifier of this specific attempt within the run.</param>
/// <param name="ArtifactUrl">URL from which the worker must download the automation artifact.</param>
/// <param name="ArtifactDigest">SHA-256 digest of the artifact; worker must verify before execution.</param>
/// <param name="LeaseDurationMs">Duration of the lease in milliseconds; worker must renew via Heartbeat before expiry.</param>
/// <param name="JobSchemaVersion">Schema version of the job artifact.</param>
public record LeaseGranted(
    [property: JsonPropertyName("leaseId")] string LeaseId,
    [property: JsonPropertyName("runId")] string RunId,
    [property: JsonPropertyName("attemptId")] string AttemptId,
    [property: JsonPropertyName("artifactUrl")] string ArtifactUrl,
    [property: JsonPropertyName("artifactDigest")] string ArtifactDigest,
    [property: JsonPropertyName("leaseDurationMs")] int LeaseDurationMs,
    [property: JsonPropertyName("jobSchemaVersion")] string JobSchemaVersion)
{
    /// <summary>Discriminator value identifying this message type.</summary>
    [JsonPropertyName("messageType")]
    public string MessageType => "LeaseGranted";
}
