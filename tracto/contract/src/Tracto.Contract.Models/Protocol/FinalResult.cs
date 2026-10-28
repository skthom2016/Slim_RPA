#nullable enable

using System.Text.Json.Serialization;
using Tracto.Contract.Models.Execution;

namespace Tracto.Contract.Models.Protocol;

/// <summary>Sent by the worker when the run has reached a terminal state; carries the full run result.</summary>
/// <param name="LeaseId">The lease identifier this result closes.</param>
/// <param name="WorkerId">Stable unique identity of the worker reporting the result.</param>
/// <param name="Result">The final run result.</param>
public record FinalResult(
    [property: JsonPropertyName("leaseId")] string LeaseId,
    [property: JsonPropertyName("workerId")] string WorkerId,
    [property: JsonPropertyName("result")] RunResult Result)
{
    /// <summary>Discriminator value identifying this message type.</summary>
    [JsonPropertyName("messageType")]
    public string MessageType => "FinalResult";
}
