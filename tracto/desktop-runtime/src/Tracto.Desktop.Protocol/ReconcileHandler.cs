#nullable enable

using Tracto.Contract.Models.Protocol;
using Tracto.Contract.Orchestrator;

namespace Tracto.Desktop.Protocol;

/// <summary>
/// Handles the reconciliation flow when the Desktop Runtime reconnects after a network disconnect.
/// Sends Reconcile RPC with journal evidence; never re-executes side-effectful actions regardless of outcome.
/// </summary>
public sealed class ReconcileHandler
{
    private readonly string _workerId;

    public ReconcileHandler(string workerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        _workerId = workerId;
    }

    /// <summary>
    /// Sends a ReconcileRequest to the Orchestrator and returns the Orchestrator's disposition.
    /// The caller must not re-execute any side-effectful action regardless of the response —
    /// reconciliation is observational only; only resumption from the journal-confirmed safe point is permitted.
    /// </summary>
    /// <param name="orch">The Orchestrator server.</param>
    /// <param name="leaseId">The lease identifier being reconciled.</param>
    /// <param name="journal">Journal store with evidence of steps already executed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <see cref="ReconcileResponse"/> from the Orchestrator.</returns>
    public async Task<ReconcileResponse> ReconcileAsync(
        IOrchestratorServer orch,
        string leaseId,
        JournalStore journal,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(orch);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseId);
        ArgumentNullException.ThrowIfNull(journal);

        var request = new ReconcileRequest(
            LeaseId:              leaseId,
            WorkerId:             _workerId,
            LastKnownStepId:      journal.LastStepId,
            LastKnownRunState:    null);

        return await orch.ReconcileAsync(request, ct).ConfigureAwait(false);
    }
}
