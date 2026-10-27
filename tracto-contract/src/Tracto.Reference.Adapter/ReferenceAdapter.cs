#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tracto.Contract.Models;
using Tracto.Contract.Models.Execution;
using Tracto.Contract.Models.Protocol;
using Tracto.Contract.Orchestrator;

namespace Tracto.Reference.Adapter;

/// <summary>
/// Minimal reference implementation of <see cref="IReferenceAdapter"/>.
/// Exercises the full worker lifecycle: register, pull, accept, heartbeat ×2, submit SUCCEEDED result.
/// The caller is responsible for enqueuing a job before invoking <see cref="ExecuteRoundTripAsync"/>.
/// </summary>
public sealed class ReferenceAdapter : IReferenceAdapter
{
    private readonly TimeProvider _clock;
    private readonly IReadOnlyList<int> _supportedSchemaMajors;

    public ReferenceAdapter(TimeProvider? clock = null, IReadOnlyList<int>? supportedSchemaMajors = null)
    {
        _clock = clock ?? TimeProvider.System;
        _supportedSchemaMajors = supportedSchemaMajors ?? new[] { 1 };
    }

    public async Task<AdapterRoundTripResult> ExecuteRoundTripAsync(
        IOrchestratorServer server,
        string workerId,
        CancellationToken cancellationToken = default)
    {
        // 1. Register
        var reg = await server.RegisterWorkerAsync(BuildRegistration(workerId), cancellationToken);
        if (!reg.IsAccepted)
            return AdapterRoundTripResult.Failure(workerId, $"Registration failed: {reg.ErrorCode} — {reg.Message}");

        // 2. Pull job
        var pull = await server.PullJobAsync(
            new PullJobRequest(workerId, null, _clock.GetUtcNow()),
            cancellationToken);

        if (pull is not JobOffered offered)
            return AdapterRoundTripResult.Failure(workerId, "No job available on pull");

        var lease = offered.Lease;

        // 3. Accept lease
        await server.AcceptLeaseAsync(
            new LeaseAccepted(lease.LeaseId, workerId, _clock.GetUtcNow()),
            cancellationToken);

        // 4. Heartbeat x2
        await server.ProcessHeartbeatAsync(
            new Heartbeat(lease.LeaseId, workerId, 0, _clock.GetUtcNow(), null, RunState.Running),
            cancellationToken);
        await server.ProcessHeartbeatAsync(
            new Heartbeat(lease.LeaseId, workerId, 1, _clock.GetUtcNow(), null, RunState.Running),
            cancellationToken);

        // 5. Submit SUCCEEDED result
        var now = _clock.GetUtcNow();
        var result = new RunResult(
            RunId:           lease.RunId,
            AttemptId:       lease.AttemptId,
            LeaseId:         lease.LeaseId,
            WorkerId:        workerId,
            RunState:        RunState.Succeeded,
            StartedAt:       now.AddSeconds(-5),
            CompletedAt:     now,
            DurationMs:      5000,
            LastHeartbeatAt: now.AddSeconds(-1),
            StepResults:     Array.Empty<StepResult>());

        await server.ProcessFinalResultAsync(new FinalResult(lease.LeaseId, workerId, result), cancellationToken);

        return new AdapterRoundTripResult(
            Succeeded:  true,
            WorkerId:   workerId,
            LeaseId:    lease.LeaseId,
            RunId:      lease.RunId,
            AttemptId:  lease.AttemptId,
            FinalState: RunState.Succeeded);
    }

    private WorkerRegistration BuildRegistration(string workerId) => new(
        WorkerId:              workerId,
        RuntimeType:           RuntimeType.Desktop,
        WorkerVersion:         "1.0.0",
        ProtocolVersion:       "1.0",
        SupportedSchemaMajors: _supportedSchemaMajors,
        CapabilityDigest:      "sha256:aabbccdd",
        HostClass:             HostClass.Interactive,
        InteractiveSessionId:  $"session-{workerId}",
        AdvertisedCapabilities: new AdvertisedCapabilities(
            ActionNamespaces:    new[] { "desktop.*" },
            PredicateNamespaces: Array.Empty<string>(),
            MaxIterations:       0),
        Health: new WorkerHealth(WorkerHealthStatus.Ready));
}
