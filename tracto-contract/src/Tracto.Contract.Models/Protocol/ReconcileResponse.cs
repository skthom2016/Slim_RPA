#nullable enable

using System.Text.Json.Serialization;

namespace Tracto.Contract.Models.Protocol;

/// <summary>Sent by the orchestrator in response to a ReconcileRequest, providing the authoritative disposition.</summary>
/// <param name="LeaseId">The lease identifier being reconciled.</param>
/// <param name="Disposition">The orchestrator's authoritative reconciliation verdict for this lease.</param>
/// <param name="FinalRunState">The definitive terminal run state if known, or null when disposition is Unknown.</param>
public record ReconcileResponse(
    [property: JsonPropertyName("leaseId")] string LeaseId,
    [property: JsonPropertyName("disposition")] ReconcileDisposition Disposition,
    [property: JsonPropertyName("finalRunState")] RunState? FinalRunState)
{
    /// <summary>Discriminator value identifying this message type.</summary>
    [JsonPropertyName("messageType")]
    public string MessageType => "ReconcileResponse";
}
