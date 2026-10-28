#nullable enable

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Tracto.Contract.Models;
using Tracto.Contract.Models.Execution;
using Tracto.Contract.Models.Protocol;
using Tracto.Contract.Orchestrator;
using Tracto.Mock.Orchestrator;
using Xunit;

namespace Tracto.Contract.Orchestrator.Tests;

public sealed class PullLeaseHeartbeatTests
{
    private static WorkerRegistration MakeDesktopWorker(
        string id = "w1",
        string sessionId = "session-1",
        HostClass hostClass = HostClass.Interactive) =>
        new WorkerRegistration(
            WorkerId: id,
            RuntimeType: RuntimeType.Desktop,
            WorkerVersion: "1.0.0",
            ProtocolVersion: "1.0",
            SupportedSchemaMajors: new[] { 1 },
            CapabilityDigest: "sha256:abc",
            HostClass: hostClass,
            InteractiveSessionId: hostClass == HostClass.Interactive ? sessionId : null,
            AdvertisedCapabilities: new AdvertisedCapabilities(
                ActionNamespaces: new[] { "desktop.*" },
                PredicateNamespaces: Array.Empty<string>(),
                MaxIterations: 100),
            Health: new WorkerHealth(WorkerHealthStatus.Ready));

    private static MockJob MakeDesktopJob(string runId = "run-1", bool interactive = true) =>
        new MockJob(
            RunId: runId,
            ScriptRuntimeType: "desktop",
            RequiredSchemaMajor: 1,
            RequiredNamespaces: new[] { "desktop.*" },
            IsInteractive: interactive,
            ArtifactUrl: "https://example.com/artifact.zip",
            ArtifactDigest: "sha256:deadbeef",
            JobSchemaVersion: "1.0.0");

    private static RunResult MakeRunResult(
        string runId, string attemptId, string leaseId, string workerId, RunState state) =>
        new RunResult(
            RunId: runId,
            AttemptId: attemptId,
            LeaseId: leaseId,
            WorkerId: workerId,
            RunState: state,
            StartedAt: DateTimeOffset.UtcNow,
            CompletedAt: DateTimeOffset.UtcNow,
            DurationMs: 0,
            LastHeartbeatAt: null,
            StepResults: Array.Empty<StepResult>());

    // ST1-10-AC01: Worker pulls → JobOffered when job enqueued
    [Fact]
    public async Task Pull_WithPendingJob_ReturnsJobOffered()
    {
        var sut = new MockOrchestrator();
        var server = (IOrchestratorServer)sut;
        await server.RegisterWorkerAsync(MakeDesktopWorker());
        sut.EnqueueJob(MakeDesktopJob());
        var response = await server.PullJobAsync(new PullJobRequest("w1", null, DateTimeOffset.UtcNow));
        response.Should().BeOfType<JobOffered>();
    }

    // ST1-10-AC01: Worker pulls → NoJob when queue empty
    [Fact]
    public async Task Pull_WithNoJob_ReturnsNoJob()
    {
        var sut = new MockOrchestrator();
        var server = (IOrchestratorServer)sut;
        await server.RegisterWorkerAsync(MakeDesktopWorker());
        // No job enqueued
        var response = await server.PullJobAsync(new PullJobRequest("w1", null, DateTimeOffset.UtcNow));
        response.Should().BeOfType<NoPendingJob>();
    }

    // ST1-10-AC02: Lease fields preserved (LeaseId, RunId, AttemptId, ArtifactUrl, ArtifactDigest all non-null/non-empty)
    [Fact]
    public async Task LeaseGranted_ContainsAllIdentityFields()
    {
        var sut = new MockOrchestrator();
        var server = (IOrchestratorServer)sut;
        await server.RegisterWorkerAsync(MakeDesktopWorker());
        sut.EnqueueJob(MakeDesktopJob("run-1"));
        var response = await server.PullJobAsync(new PullJobRequest("w1", null, DateTimeOffset.UtcNow));
        var offered = response.Should().BeOfType<JobOffered>().Subject;
        offered.Lease.LeaseId.Should().NotBeNullOrEmpty();
        offered.Lease.RunId.Should().Be("run-1");
        offered.Lease.AttemptId.Should().NotBeNullOrEmpty();
        offered.Lease.ArtifactUrl.Should().NotBeNullOrEmpty();
        offered.Lease.ArtifactDigest.Should().NotBeNullOrEmpty();
    }

    // ST1-10-AC03: Worker with active lease cannot accept a second job
    [Fact]
    public async Task WorkerWithActiveLease_CannotPullSecondJob()
    {
        var sut = new MockOrchestrator();
        var server = (IOrchestratorServer)sut;
        await server.RegisterWorkerAsync(MakeDesktopWorker());
        sut.EnqueueJob(MakeDesktopJob("run-1"));
        sut.EnqueueJob(MakeDesktopJob("run-2"));
        // First pull — worker becomes busy
        var first = await server.PullJobAsync(new PullJobRequest("w1", null, DateTimeOffset.UtcNow));
        first.Should().BeOfType<JobOffered>();
        // Second pull — worker is busy, must be refused
        var second = await server.PullJobAsync(new PullJobRequest("w1", null, DateTimeOffset.UtcNow));
        second.Should().BeOfType<NoPendingJob>("a worker holding an active lease must not receive a second job");
    }

    // ST1-10-AC04: Heartbeat with increasing sequence → accepted
    [Fact]
    public async Task Heartbeat_MonotonicSequence_Accepted()
    {
        var sut = new MockOrchestrator();
        var server = (IOrchestratorServer)sut;
        await server.RegisterWorkerAsync(MakeDesktopWorker());
        sut.EnqueueJob(MakeDesktopJob());
        var pull = (JobOffered)await server.PullJobAsync(new PullJobRequest("w1", null, DateTimeOffset.UtcNow));
        var leaseId = pull.Lease.LeaseId;

        var hb0 = new Heartbeat(leaseId, "w1", 0, DateTimeOffset.UtcNow, null, RunState.Running);
        var ack0 = await server.ProcessHeartbeatAsync(hb0);
        ack0.SequenceNumber.Should().Be(0);

        var hb1 = new Heartbeat(leaseId, "w1", 1, DateTimeOffset.UtcNow, null, RunState.Running);
        var ack1 = await server.ProcessHeartbeatAsync(hb1);
        ack1.SequenceNumber.Should().Be(1);
    }

    // ST1-10-AC04: Duplicate heartbeat sequence does not regress state
    [Fact]
    public async Task Heartbeat_DuplicateSequence_DoesNotRegressState()
    {
        var sut = new MockOrchestrator();
        var server = (IOrchestratorServer)sut;
        await server.RegisterWorkerAsync(MakeDesktopWorker());
        sut.EnqueueJob(MakeDesktopJob());
        var pull = (JobOffered)await server.PullJobAsync(new PullJobRequest("w1", null, DateTimeOffset.UtcNow));
        var leaseId = pull.Lease.LeaseId;

        await server.ProcessHeartbeatAsync(new Heartbeat(leaseId, "w1", 0, DateTimeOffset.UtcNow, null, RunState.Running));
        await server.ProcessHeartbeatAsync(new Heartbeat(leaseId, "w1", 1, DateTimeOffset.UtcNow, null, RunState.Running));

        // Send duplicate sequence number — TryAdvanceSequence should return false (no backwards move)
        var ackDup = await server.ProcessHeartbeatAsync(
            new Heartbeat(leaseId, "w1", 1, DateTimeOffset.UtcNow, null, RunState.Running));
        // Ack is still returned with the echoed sequence; state is not regressed
        ackDup.SequenceNumber.Should().Be(1, "the duplicate sequence is echoed back");
        ackDup.CancellationRequested.Should().BeFalse("no cancellation was requested");
    }

    // ST1-10-AC05: RequestCancellation sets flag; next heartbeat ack has CancellationRequested=true
    [Fact]
    public async Task LostHeartbeat_SimulatedByRequestCancellation_AckCancels()
    {
        var sut = new MockOrchestrator();
        var server = (IOrchestratorServer)sut;
        await server.RegisterWorkerAsync(MakeDesktopWorker());
        sut.EnqueueJob(MakeDesktopJob());
        var pull = (JobOffered)await server.PullJobAsync(new PullJobRequest("w1", null, DateTimeOffset.UtcNow));
        var leaseId = pull.Lease.LeaseId;

        sut.RequestCancellation(leaseId, "simulated expiry");

        var ack = await server.ProcessHeartbeatAsync(
            new Heartbeat(leaseId, "w1", 0, DateTimeOffset.UtcNow, null, RunState.Running));
        ack.CancellationRequested.Should().BeTrue("orchestrator cancellation must be delivered in the heartbeat ack");
    }

    // ST1-10-AC06: Stale attempt result is ignored — w2 submits FinalResult for w1's lease
    [Fact]
    public async Task StaleAttemptResult_IsIgnored()
    {
        var sut = new MockOrchestrator();
        var server = (IOrchestratorServer)sut;
        await server.RegisterWorkerAsync(MakeDesktopWorker("w1", "session-1"));
        await server.RegisterWorkerAsync(MakeDesktopWorker("w2", "session-2"));
        sut.EnqueueJob(MakeDesktopJob("run-1"));
        var pull = (JobOffered)await server.PullJobAsync(new PullJobRequest("w1", null, DateTimeOffset.UtcNow));
        var leaseId = pull.Lease.LeaseId;
        var runId = pull.Lease.RunId;
        var attemptId = pull.Lease.AttemptId;

        // w2 submits FinalResult for w1's leaseId — this is a stale attempt
        var stale = new FinalResult(
            LeaseId: leaseId,
            WorkerId: "w2", // w2 does not hold this lease
            Result: MakeRunResult(runId, attemptId, leaseId, "w2", RunState.Succeeded));
        await server.ProcessFinalResultAsync(stale);

        // w1's lease must still be active (reconcile returns Unknown, not AlreadyTerminal)
        var reconcile = await server.ReconcileAsync(
            new ReconcileRequest(leaseId, "w1", null, null));
        reconcile.Disposition.Should().Be(ReconcileDisposition.Unknown,
            "stale result must be ignored; w1 still holds the lease");
    }

    // ST1-10-AC07: Transport portability — MockOrchestrator implements IOrchestratorServer (compile-time check via cast)
    [Fact]
    public void MockOrchestrator_ImplementsIOrchestratorServer()
    {
        var mock = new MockOrchestrator();
        // This cast is a compile-time guarantee that MockOrchestrator satisfies the interface contract.
        IOrchestratorServer server = mock;
        server.Should().NotBeNull("MockOrchestrator must implement IOrchestratorServer");
    }

    // ST1-10-AC08: Journal recovery via ReconcileAsync — active lease returns Unknown
    [Fact]
    public async Task Reconcile_ActiveLease_ReturnsUnknown()
    {
        var sut = new MockOrchestrator();
        var server = (IOrchestratorServer)sut;
        await server.RegisterWorkerAsync(MakeDesktopWorker());
        sut.EnqueueJob(MakeDesktopJob());
        var pull = (JobOffered)await server.PullJobAsync(new PullJobRequest("w1", null, DateTimeOffset.UtcNow));
        var leaseId = pull.Lease.LeaseId;

        // Reconcile without submitting FinalResult — lease is still active
        var response = await server.ReconcileAsync(
            new ReconcileRequest(leaseId, "w1", null, null));
        response.Disposition.Should().Be(ReconcileDisposition.Unknown,
            "an active lease with no terminal state must reconcile as Unknown");
    }
}
