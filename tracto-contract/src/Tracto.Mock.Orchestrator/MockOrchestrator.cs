#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tracto.Contract.Models;
using Tracto.Contract.Models.Protocol;
using Tracto.Contract.Orchestrator;
using Tracto.Contract.Validation;

namespace Tracto.Mock.Orchestrator;

/// <summary>
/// In-process mock implementation of <see cref="IOrchestratorServer"/> for use in tests
/// and local integration harnesses. Thread-safe; all collections delegate to
/// <see cref="MockOrchestratorCore"/> which uses ConcurrentDictionary throughout.
/// </summary>
public sealed class MockOrchestrator : IOrchestratorServer
{
    private readonly MockOrchestratorCore _core = new();

    /// <summary>
    /// Optional validator; when null, schema/capability validation is skipped and only
    /// the interactive-session-id guard (C4 Rule 4) is enforced inline.
    /// </summary>
    private readonly WorkerRegistrationValidator? _registrationValidator;
    private readonly TimeProvider _clock;

    /// <param name="registrationValidator">
    ///   Pre-initialised validator; pass <c>null</c> to skip schema validation (useful
    ///   for tests that do not want to load JSON schema resources).
    ///   Use <see cref="WorkerRegistrationValidator.CreateAsync"/> to create a fully
    ///   initialised instance when validation is required.
    /// </param>
    /// <param name="clock">Replaceable clock; defaults to <see cref="TimeProvider.System"/>.</param>
    public MockOrchestrator(WorkerRegistrationValidator? registrationValidator = null,
                            TimeProvider?                clock                 = null)
    {
        _registrationValidator = registrationValidator;
        _clock                 = clock ?? TimeProvider.System;
    }

    // -----------------------------------------------------------------------
    // Convenience helpers
    // -----------------------------------------------------------------------

    /// <summary>Enqueues a job so that a matching worker may pull it.</summary>
    /// <remarks>
    /// <see cref="MockJob"/> is internal to this assembly; callers in other assemblies
    /// require an <c>InternalsVisibleTo</c> entry in the project file.
    /// </remarks>
    internal void EnqueueJob(MockJob job) => _core.EnqueueJob(job);

    /// <summary>Requests cancellation of a running lease.</summary>
    public void RequestCancellation(string leaseId, string? reason = null)
        => _core.RequestCancellation(leaseId, reason);

    // -----------------------------------------------------------------------
    // IOrchestratorServer implementation
    // -----------------------------------------------------------------------

    Task<RegistrationResult> IOrchestratorServer.RegisterWorkerAsync(
        WorkerRegistration registration, CancellationToken ct)
    {
        // 1. Run WorkerRegistrationValidator when one was supplied.
        if (_registrationValidator is not null)
        {
            var result = _registrationValidator.ValidateObject(registration);
            if (!result.IsValid)
            {
                var first = result.Diagnostics.First(d => d.Severity == ValidationSeverity.Error);
                return Task.FromResult(RegistrationResult.Refused(first.Code, first.Message));
            }
        }

        // 2. Interactive workers must supply interactiveSessionId (C4 Rule 4).
        //    This guard is always enforced even when the full validator is absent.
        if (registration.HostClass == HostClass.Interactive
            && string.IsNullOrEmpty(registration.InteractiveSessionId))
        {
            return Task.FromResult(RegistrationResult.Refused(
                "MISSING_INTERACTIVE_SESSION_ID",
                "Interactive workers must supply interactiveSessionId."));
        }

        // 3. Register (upsert — re-registration replaces the previous record).
        _core.Workers[registration.WorkerId] = new WorkerState { Registration = registration };
        return Task.FromResult(RegistrationResult.Accepted(registration.WorkerId));
    }

    Task<PullJobResponse> IOrchestratorServer.PullJobAsync(
        PullJobRequest request, CancellationToken ct)
    {
        if (!_core.Workers.TryGetValue(request.WorkerId, out var worker))
            return Task.FromResult<PullJobResponse>(new NoPendingJob(new NoJobAvailable(5_000)));

        // Worker is already executing a lease — back off.
        if (worker.IsBusy)
            return Task.FromResult<PullJobResponse>(new NoPendingJob(new NoJobAvailable(1_000)));

        if (!_core.TryDequeueMatchingJob(worker.Registration, out var job) || job is null)
            return Task.FromResult<PullJobResponse>(new NoPendingJob(new NoJobAvailable(2_000)));

        // Interactive session exclusivity (C4): only one concurrent lease per session.
        if (job.IsInteractive)
        {
            var sessionId = worker.Registration.InteractiveSessionId;
            if (string.IsNullOrEmpty(sessionId))
                return Task.FromResult<PullJobResponse>(new NoPendingJob(new NoJobAvailable(1_000)));

            var leaseId = Guid.NewGuid().ToString("N");
            if (!_core.SessionEnforcer.TryAcquire(sessionId, leaseId))
            {
                // Another worker already holds this session — put the job back.
                _core.EnqueueJob(job);
                return Task.FromResult<PullJobResponse>(new NoPendingJob(new NoJobAvailable(1_000)));
            }

            return CreateLease(worker, job, leaseId, sessionId: sessionId);
        }

        return CreateLease(worker, job, Guid.NewGuid().ToString("N"), sessionId: null);
    }

    private Task<PullJobResponse> CreateLease(
        WorkerState worker, MockJob job, string leaseId, string? sessionId)
    {
        var attemptId = Guid.NewGuid().ToString("N");

        var record = new LeaseRecord
        {
            LeaseId       = leaseId,
            RunId         = job.RunId,
            AttemptId     = attemptId,
            WorkerId      = worker.Registration.WorkerId,
            IsInteractive = job.IsInteractive,
            SessionId     = sessionId,
        };

        _core.Leases[leaseId] = record;
        worker.ActiveLeaseId  = leaseId;

        var lease = new LeaseGranted(
            LeaseId:          leaseId,
            RunId:            job.RunId,
            AttemptId:        attemptId,
            ArtifactUrl:      job.ArtifactUrl,
            ArtifactDigest:   job.ArtifactDigest,
            LeaseDurationMs:  job.LeaseDurationMs,
            JobSchemaVersion: job.JobSchemaVersion);

        return Task.FromResult<PullJobResponse>(new JobOffered(lease));
    }

    Task IOrchestratorServer.AcceptLeaseAsync(LeaseAccepted accepted, CancellationToken ct)
    {
        // Validate lease exists and belongs to the worker.
        // Silent accept is sufficient for the mock.
        return Task.CompletedTask;
    }

    Task<HeartbeatAck> IOrchestratorServer.ProcessHeartbeatAsync(
        Heartbeat heartbeat, CancellationToken ct)
    {
        if (!_core.Leases.TryGetValue(heartbeat.LeaseId, out var record))
            return Task.FromResult(new HeartbeatAck(heartbeat.LeaseId, heartbeat.SequenceNumber, false));

        // Monotonic sequence guard (ST1-10-AC04).
        record.TryAdvanceSequence(heartbeat.SequenceNumber);

        bool cancel = record.CancellationRequested;
        return Task.FromResult(new HeartbeatAck(heartbeat.LeaseId, heartbeat.SequenceNumber, cancel));
    }

    Task IOrchestratorServer.ProcessFinalResultAsync(FinalResult result, CancellationToken ct)
    {
        if (!_core.Leases.TryGetValue(result.LeaseId, out var record))
            return Task.CompletedTask;

        // Attempt fencing: reject stale results (ST1-10-AC06).
        // The LeaseId must match the worker's current active lease.
        if (!_core.Workers.TryGetValue(result.WorkerId, out var worker)
            || worker.ActiveLeaseId != result.LeaseId)
        {
            return Task.CompletedTask;   // stale — ignore
        }

        record.MarkTerminal(result.Result.RunState);
        worker.ActiveLeaseId = null;

        // Release interactive session lock.
        if (record.IsInteractive && !string.IsNullOrEmpty(record.SessionId))
            _core.SessionEnforcer.Release(record.SessionId, record.LeaseId);

        return Task.CompletedTask;
    }

    Task<ReconcileResponse> IOrchestratorServer.ReconcileAsync(
        ReconcileRequest request, CancellationToken ct)
    {
        if (!_core.Leases.TryGetValue(request.LeaseId, out var record))
            return Task.FromResult(new ReconcileResponse(
                request.LeaseId, ReconcileDisposition.Unknown, null));

        if (record.IsTerminal)
            return Task.FromResult(new ReconcileResponse(
                request.LeaseId, ReconcileDisposition.AlreadyTerminal, record.TerminalState));

        // If cancelled without uncertain side-effect → SAFE_TO_REQUEUE.
        if (record.CancellationRequested && request.LastKnownRunState != "UNKNOWN")
            return Task.FromResult(new ReconcileResponse(
                request.LeaseId, ReconcileDisposition.SafeToRequeue, null));

        // Default: cannot establish commitment → UNKNOWN.
        return Task.FromResult(new ReconcileResponse(
            request.LeaseId, ReconcileDisposition.Unknown, null));
    }
}
