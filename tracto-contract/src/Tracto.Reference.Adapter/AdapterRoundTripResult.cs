#nullable enable

using Tracto.Contract.Models;

namespace Tracto.Reference.Adapter;

/// <summary>Describes the outcome of a full worker lifecycle round-trip executed by <see cref="IReferenceAdapter"/>.</summary>
public sealed record AdapterRoundTripResult(
    bool Succeeded,
    string WorkerId,
    string? LeaseId,
    string? RunId,
    string? AttemptId,
    RunState? FinalState,
    string? FailureReason = null)
{
    /// <summary>Creates a failure result with no lease, run, or attempt information.</summary>
    public static AdapterRoundTripResult Failure(string workerId, string reason)
        => new(false, workerId, null, null, null, null, reason);
}
