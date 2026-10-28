#nullable enable

using System.Threading;
using System.Threading.Tasks;
using Tracto.Contract.Orchestrator;

namespace Tracto.Reference.Adapter;

/// <summary>
/// Contract for a reference adapter that exercises the full worker lifecycle round-trip
/// against an <see cref="IOrchestratorServer"/>.
/// </summary>
public interface IReferenceAdapter
{
    /// <summary>
    /// Executes the full worker lifecycle round-trip:
    /// register → pull → accept → heartbeat × N → submit result.
    /// </summary>
    /// <param name="server">The orchestrator server to execute against.</param>
    /// <param name="workerId">The worker identifier to use during registration.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A description of what happened (for report logging).</returns>
    Task<AdapterRoundTripResult> ExecuteRoundTripAsync(
        IOrchestratorServer server,
        string workerId,
        CancellationToken cancellationToken = default);
}
