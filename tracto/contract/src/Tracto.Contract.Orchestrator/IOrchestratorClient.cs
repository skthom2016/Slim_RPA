#nullable enable

using System.Threading;
using System.Threading.Tasks;
using Tracto.Contract.Models.Protocol;

namespace Tracto.Contract.Orchestrator;

/// <summary>
/// Client-side abstraction for the orchestrator, mirroring <see cref="IOrchestratorServer"/>.
/// Lets tests swap between in-process and HTTP transport without changing protocol code.
/// </summary>
public interface IOrchestratorClient
{
    /// <summary>Registers a worker with the orchestrator.</summary>
    Task<RegistrationResult> RegisterWorkerAsync(WorkerRegistration registration, CancellationToken cancellationToken = default);

    /// <summary>Attempts to pull the next available job for a worker.</summary>
    Task<PullJobResponse> PullJobAsync(PullJobRequest request, CancellationToken cancellationToken = default);

    /// <summary>Acknowledges that the worker has accepted the granted lease.</summary>
    Task AcceptLeaseAsync(LeaseAccepted accepted, CancellationToken cancellationToken = default);

    /// <summary>Processes a heartbeat from an active worker.</summary>
    Task<HeartbeatAck> ProcessHeartbeatAsync(Heartbeat heartbeat, CancellationToken cancellationToken = default);

    /// <summary>Records the final execution result for a completed job.</summary>
    Task ProcessFinalResultAsync(FinalResult result, CancellationToken cancellationToken = default);

    /// <summary>Reconciles worker state with the orchestrator's view.</summary>
    Task<ReconcileResponse> ReconcileAsync(ReconcileRequest request, CancellationToken cancellationToken = default);
}
