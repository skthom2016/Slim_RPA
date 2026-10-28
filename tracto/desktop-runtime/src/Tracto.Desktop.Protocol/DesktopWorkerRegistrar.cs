#nullable enable

using Tracto.Contract.Models;
using Tracto.Contract.Models.Protocol;
using Tracto.Contract.Orchestrator;

namespace Tracto.Desktop.Protocol;

/// <summary>
/// Registers the Desktop Runtime worker with the Orchestrator at startup.
/// Sends a <see cref="WorkerRegistration"/> with runtimeType=desktop and the computed capabilityDigest.
/// </summary>
public sealed class DesktopWorkerRegistrar
{
    private readonly string _workerId;
    private readonly string _workerVersion;
    private readonly string _interactiveSessionId;
    private readonly WorkerCapabilities _capabilities;

    /// <summary>
    /// Creates a new registrar.
    /// </summary>
    /// <param name="workerId">Stable unique identity of this worker.</param>
    /// <param name="workerVersion">Semantic version of this worker binary.</param>
    /// <param name="interactiveSessionId">Windows interactive session identifier (required for desktop workers).</param>
    /// <param name="capabilities">Capability advertisement; defaults to <see cref="WorkerCapabilities"/> defaults.</param>
    public DesktopWorkerRegistrar(
        string workerId,
        string workerVersion,
        string interactiveSessionId,
        WorkerCapabilities? capabilities = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(interactiveSessionId);

        _workerId              = workerId;
        _workerVersion         = workerVersion;
        _interactiveSessionId  = interactiveSessionId;
        _capabilities          = capabilities ?? new WorkerCapabilities();
    }

    /// <summary>
    /// Sends a RegisterWorker message to the Orchestrator and returns the registration result.
    /// Throws <see cref="InvalidOperationException"/> when registration is refused.
    /// </summary>
    public async Task<RegistrationResult> RegisterAsync(IOrchestratorServer orch, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(orch);

        string digest = CapabilityDigestCalculator.Compute(_capabilities);

        var registration = new WorkerRegistration(
            WorkerId:              _workerId,
            RuntimeType:           RuntimeType.Desktop,
            WorkerVersion:         _workerVersion,
            ProtocolVersion:       "1.0",
            SupportedSchemaMajors: _capabilities.SupportedSchemaMajors,
            CapabilityDigest:      digest,
            HostClass:             HostClass.Interactive,
            InteractiveSessionId:  _interactiveSessionId,
            AdvertisedCapabilities: new AdvertisedCapabilities(
                ActionNamespaces:    _capabilities.ActionNamespaces,
                PredicateNamespaces: _capabilities.PredicateNamespaces,
                MaxIterations:       _capabilities.MaxIterations),
            Health: new WorkerHealth(WorkerHealthStatus.Ready));

        var result = await orch.RegisterWorkerAsync(registration, ct).ConfigureAwait(false);
        return result;
    }
}
