#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Tracto.Contract.Models;
using Tracto.Contract.Models.Protocol;
using Tracto.Contract.Orchestrator;

namespace Tracto.Mock.Orchestrator;

/// <summary>
/// Core state holder and job-routing logic for the mock orchestrator.
/// All collections are thread-safe; no external locking is required by callers.
/// </summary>
internal sealed class MockOrchestratorCore
{
    // -----------------------------------------------------------------------
    // Public collections (accessible to the server implementation layer)
    // -----------------------------------------------------------------------

    internal ConcurrentDictionary<string, WorkerState>  Workers              { get; } = new();
    internal ConcurrentDictionary<string, LeaseRecord>  Leases               { get; } = new();
    internal ConcurrentDictionary<string, CancelRequest> PendingCancellations { get; } = new();
    internal InteractiveLeaseEnforcer SessionEnforcer { get; } = new();

    // -----------------------------------------------------------------------
    // Private job queue
    // -----------------------------------------------------------------------

    private readonly ConcurrentQueue<MockJob> _jobQueue = new();

    // -----------------------------------------------------------------------
    // Public operations
    // -----------------------------------------------------------------------

    /// <summary>Adds a job to the internal queue for dispatch.</summary>
    public void EnqueueJob(MockJob job)
    {
        _jobQueue.Enqueue(job);
    }

    /// <summary>
    /// Sets the cancellation flag on the matching lease and queues a
    /// <see cref="CancelRequest"/> for delivery on the next heartbeat acknowledgement.
    /// </summary>
    public void RequestCancellation(string leaseId, string? reason = null)
    {
        if (Leases.TryGetValue(leaseId, out var record))
        {
            record.CancellationRequested = true;
        }

        var cancel = new CancelRequest(
            LeaseId:     leaseId,
            RequestedAt: DateTimeOffset.UtcNow,
            Reason:      reason);

        PendingCancellations[leaseId] = cancel;
    }

    /// <summary>
    /// Inspects the queue and returns the first job whose requirements are fully
    /// satisfied by <paramref name="worker"/>. Returns <c>false</c> when no
    /// matching job exists; does not remove non-matching jobs from the queue.
    /// </summary>
    public bool TryDequeueMatchingJob(WorkerRegistration worker, out MockJob? job)
    {
        // Collect items we peek at but do not match so we can put them back.
        var skipped = new List<MockJob>();
        job = null;

        while (_jobQueue.TryDequeue(out var candidate))
        {
            if (IsMatch(candidate, worker))
            {
                job = candidate;
                break;
            }
            skipped.Add(candidate);
        }

        // Re-enqueue skipped jobs (order preserved as best-effort in a concurrent env).
        foreach (var s in skipped)
        {
            _jobQueue.Enqueue(s);
        }

        return job is not null;
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private static bool IsMatch(MockJob job, WorkerRegistration worker)
    {
        // 1. RuntimeType must match (case-insensitive string comparison against enum name).
        if (!string.Equals(
                job.ScriptRuntimeType,
                worker.RuntimeType.ToString(),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 2. Required schema major must be in worker's supported list.
        bool schemaCovered = false;
        foreach (var major in worker.SupportedSchemaMajors)
        {
            if (major == job.RequiredSchemaMajor)
            {
                schemaCovered = true;
                break;
            }
        }
        if (!schemaCovered) return false;

        // 3. All required namespaces must be covered by the worker's advertised patterns.
        foreach (var required in job.RequiredNamespaces)
        {
            if (!IsNamespaceCovered(required, worker.AdvertisedCapabilities.ActionNamespaces))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns true when <paramref name="required"/> is covered by at least one
    /// pattern in <paramref name="patterns"/> using exact match or prefix-glob
    /// (e.g. "actions.ui.*" covers "actions.ui.click").
    /// </summary>
    private static bool IsNamespaceCovered(string required, IReadOnlyList<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (string.Equals(required, pattern, StringComparison.Ordinal))
            {
                return true;
            }

            if (pattern.EndsWith('*'))
            {
                var prefix = pattern.TrimEnd('*');
                if (required.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        return false;
    }
}
