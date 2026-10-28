#nullable enable

using System.Text.Json.Serialization;

namespace Tracto.Contract.Models.Protocol;

/// <summary>Sent by the worker when it needs the orchestrator to determine the authoritative disposition of a run.</summary>
/// <param name="LeaseId">The lease identifier being reconciled.</param>
/// <param name="WorkerId">Stable unique identity of the worker requesting reconciliation.</param>
/// <param name="LastKnownStepId">The last step identifier the worker had observed, or null if no step was reached.</param>
/// <param name="LastKnownRunState">The last run state the worker had observed, or null if unknown.</param>
public record ReconcileRequest(
    [property: JsonPropertyName("leaseId")] string LeaseId,
    [property: JsonPropertyName("workerId")] string WorkerId,
    [property: JsonPropertyName("lastKnownStepId")] string? LastKnownStepId,
    [property: JsonPropertyName("lastKnownRunState")] string? LastKnownRunState)
{
    /// <summary>Discriminator value identifying this message type.</summary>
    [JsonPropertyName("messageType")]
    public string MessageType => "ReconcileRequest";
}
