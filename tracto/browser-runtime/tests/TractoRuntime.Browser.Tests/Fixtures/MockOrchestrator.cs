using System.Collections.Concurrent;
using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Tests.Fixtures;

/// <summary>In-process mock Orchestrator for unit/integration tests.
/// Supports registration, lease, heartbeat, and callback simulation.</summary>
public sealed class MockOrchestrator
{
    private readonly ConcurrentDictionary<string, WorkerRegistration> _workers = new();
    private readonly ConcurrentDictionary<string, LeaseAcquireResult> _leases = new();
    private readonly ConcurrentQueue<CecRunRequest> _dispatchQueue = new();
    private readonly ConcurrentDictionary<string, CecRunResult> _results = new();

    public IReadOnlyDictionary<string, WorkerRegistration> RegisteredWorkers => _workers;

    public void RegisterWorker(WorkerRegistration registration)
        => _workers[registration.WorkerId] = registration;

    public LeaseAcquireResult AcquireLease(LeaseAcquireRequest request)
    {
        var result = new LeaseAcquireResult
        {
            LeaseToken = $"lease-{Guid.NewGuid():N}",
            ExpiresAt = DateTimeOffset.UtcNow.Add(request.LeaseDuration)
        };
        _leases[result.LeaseToken] = result;
        return result;
    }

    public void HeartbeatLease(HeartbeatRequest request)
    {
        if (!_leases.ContainsKey(request.LeaseToken))
            throw new InvalidOperationException($"Unknown lease token: {request.LeaseToken}");
    }

    public void Enqueue(CecRunRequest request) => _dispatchQueue.Enqueue(request);

    public bool TryDequeue(out CecRunRequest? request) => _dispatchQueue.TryDequeue(out request);

    public void RecordResult(CecRunResult result) => _results[result.RunId] = result;

    public CecRunResult? GetResult(string runId)
        => _results.TryGetValue(runId, out var r) ? r : null;

    public CallbackNotification CreateCallback(string runId, ServiceOutcome outcome)
        => new()
        {
            RunId = runId,
            CallbackToken = $"cb-{Guid.NewGuid():N}",
            AuthTag = $"auth-{Guid.NewGuid():N}",
            ServiceOutcome = outcome
        };
}
