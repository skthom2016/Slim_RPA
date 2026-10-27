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

public sealed class ReconcileAndCancelTests
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

    // ST1-11-AC01: Cancellation delivered in heartbeat ack
    [Fact]
    public async Task CancelRequest_DeliveredInHeartbeatAck()
    {
        var sut = new MockOrchestrator();
        var server = (IOrchestratorServer)sut;
        await server.RegisterWorkerAsync(MakeDesktopWorker());
        sut.EnqueueJob(MakeDesktopJob());
        var pull = (JobOffered)await server.PullJobAsync(new PullJobRequest("w1", null, DateTimeOffset.UtcNow));
        var leaseId = pull.Lease.LeaseId;

        sut.RequestCancellation(leaseId);

        var ack = await server.ProcessHeartbeatAsync(
            new Heartbeat(leaseId, "w1", 0, DateTimeOffset.UtcNow, null, RunState.Running));
        ack.CancellationRequested.Should().BeTrue("cancel request must be delivered via heartbeat ack");
    }

    // ST1-11-AC02: After cancel, worker submits CANCELLED FinalResult → terminal; Reconcile → ALREADY_TERMINAL
    [Fact]
    public async Task CancelledRun_FinalResult_MarksTerminal()
    {
        var sut = new MockOrchestrator();
        var server = (IOrchestratorServer)sut;
        await server.RegisterWorkerAsync(MakeDesktopWorker());
        sut.EnqueueJob(MakeDesktopJob("run-1"));
        var pull = (JobOffered)await server.PullJobAsync(new PullJobRequest("w1", null, DateTimeOffset.UtcNow));
        var leaseId = pull.Lease.LeaseId;
        var runId = pull.Lease.RunId;
        var attemptId = pull.Lease.AttemptId;

        sut.RequestCancellation(leaseId);

        await server.ProcessFinalResultAsync(new FinalResult(
            LeaseId: leaseId,
            WorkerId: "w1",
            Result: MakeRunResult(runId, attemptId, leaseId, "w1", RunState.Cancelled)));

        var response = await server.ReconcileAsync(
            new ReconcileRequest(leaseId, "w1", null, null));
        response.Disposition.Should().Be(ReconcileDisposition.AlreadyTerminal,
            "CANCELLED FinalResult must mark the lease as terminal");
        response.FinalRunState.Should().Be(RunState.Cancelled);
    }

    // ST1-11-AC03: Uncertain side effect → UNKNOWN (worker pulled but reconciles with LastKnownRunState=UNKNOWN)
    [Fact]
    public async Task UncertainSideEffect_ReconcileReturnsUnknown()
    {
        var sut = new MockOrchestrator();
        var server = (IOrchestratorServer)sut;
        await server.RegisterWorkerAsync(MakeDesktopWorker());
        sut.EnqueueJob(MakeDesktopJob());
        var pull = (JobOffered)await server.PullJobAsync(new PullJobRequest("w1", null, DateTimeOffset.UtcNow));
        var leaseId = pull.Lease.LeaseId;

        var response = await server.ReconcileAsync(
            new ReconcileRequest(leaseId, "w1", null, "UNKNOWN"));
        response.Disposition.Should().Be(ReconcileDisposition.Unknown,
            "uncertain side-effect state must reconcile as Unknown");
    }

    // ST1-11-AC04: Idempotent retry — Reconcile → SafeToRequeue, then re-enqueue same runId → new lease
    [Fact]
    public async Task SafeToRequeue_Disposition_AllowsRequeue()
    {
        var sut = new MockOrchestrator();
        var server = (IOrchestratorServer)sut;
        await server.RegisterWorkerAsync(MakeDesktopWorker());
        sut.EnqueueJob(MakeDesktopJob("run-1"));
        var pull = (JobOffered)await server.PullJobAsync(new PullJobRequest("w1", null, DateTimeOffset.UtcNow));
        var leaseId = pull.Lease.LeaseId;
        var runId = pull.Lease.RunId;
        var attemptId = pull.Lease.AttemptId;

        // Cancel and reconcile with a known (non-UNKNOWN) state → SafeToRequeue
        sut.RequestCancellation(leaseId);
        var reconcile = await server.ReconcileAsync(
            new ReconcileRequest(leaseId, "w1", null, "Running"));
        reconcile.Disposition.Should().Be(ReconcileDisposition.SafeToRequeue);

        // Worker acknowledges cancellation by submitting CANCELLED FinalResult (frees the worker)
        await server.ProcessFinalResultAsync(new FinalResult(
            LeaseId: leaseId,
            WorkerId: "w1",
            Result: MakeRunResult(runId, attemptId, leaseId, "w1", RunState.Cancelled)));

        // Re-enqueue the same runId (simulating orchestrator re-queue after SafeToRequeue)
        sut.EnqueueJob(MakeDesktopJob("run-1"));

        // Worker can now pull the re-queued job and receive a new lease
        var repull = await server.PullJobAsync(new PullJobRequest("w1", null, DateTimeOffset.UtcNow));
        repull.Should().BeOfType<JobOffered>("the re-queued runId must be dispatched with a new lease");
        var newLease = ((JobOffered)repull).Lease;
        newLease.RunId.Should().Be("run-1");
        newLease.LeaseId.Should().NotBe(leaseId, "the new lease must have a different id");
    }

    // ST1-11-AC05: Non-idempotent cancel with UNKNOWN state → stays Unknown (not SafeToRequeue)
    [Fact]
    public async Task NonIdempotentCancel_WithUnknownState_Stays_Unknown()
    {
        var sut = new MockOrchestrator();
        var server = (IOrchestratorServer)sut;
        await server.RegisterWorkerAsync(MakeDesktopWorker());
        sut.EnqueueJob(MakeDesktopJob());
        var pull = (JobOffered)await server.PullJobAsync(new PullJobRequest("w1", null, DateTimeOffset.UtcNow));
        var leaseId = pull.Lease.LeaseId;

        sut.RequestCancellation(leaseId);

        // LastKnownRunState = "UNKNOWN" — the SafeToRequeue guard does not fire
        var response = await server.ReconcileAsync(
            new ReconcileRequest(leaseId, "w1", null, "UNKNOWN"));
        response.Disposition.Should().Be(ReconcileDisposition.Unknown,
            "uncertain side-effect prevents SafeToRequeue when LastKnownRunState is UNKNOWN");
    }

    // ST1-11-AC06: Unknown leaseId reconciles as Unknown (no state = cannot confirm)
    [Fact]
    public async Task UnknownLeaseId_Reconcile_ReturnsUnknown()
    {
        var sut = new MockOrchestrator(); // fresh orchestrator — no lease records
        var server = (IOrchestratorServer)sut;
        var response = await server.ReconcileAsync(
            new ReconcileRequest("lease-does-not-exist", "w1", null, null));
        response.Disposition.Should().Be(ReconcileDisposition.Unknown,
            "an unknown leaseId cannot be confirmed as safe or terminal");
    }

    // ST1-11-AC07: Bounded recovery — TIMED_OUT FinalResult → terminal; Reconcile → ALREADY_TERMINAL
    [Fact]
    public async Task TimedOut_FinalResult_MarksTerminal_AlreadyTerminal()
    {
        var sut = new MockOrchestrator();
        var server = (IOrchestratorServer)sut;
        await server.RegisterWorkerAsync(MakeDesktopWorker());
        sut.EnqueueJob(MakeDesktopJob("run-1"));
        var pull = (JobOffered)await server.PullJobAsync(new PullJobRequest("w1", null, DateTimeOffset.UtcNow));
        var leaseId = pull.Lease.LeaseId;
        var runId = pull.Lease.RunId;
        var attemptId = pull.Lease.AttemptId;

        await server.ProcessFinalResultAsync(new FinalResult(
            LeaseId: leaseId,
            WorkerId: "w1",
            Result: MakeRunResult(runId, attemptId, leaseId, "w1", RunState.TimedOut)));

        var response = await server.ReconcileAsync(
            new ReconcileRequest(leaseId, "w1", null, null));
        response.Disposition.Should().Be(ReconcileDisposition.AlreadyTerminal,
            "TIMED_OUT FinalResult must mark the lease as terminal");
        response.FinalRunState.Should().Be(RunState.TimedOut);
    }

    // ST1-11-AC08: UNKNOWN FinalResult → terminal; Reconcile → ALREADY_TERMINAL with FinalRunState=Unknown
    [Fact]
    public async Task UnknownFinalResult_Reconcile_AlreadyTerminal_WithState()
    {
        var sut = new MockOrchestrator();
        var server = (IOrchestratorServer)sut;
        await server.RegisterWorkerAsync(MakeDesktopWorker());
        sut.EnqueueJob(MakeDesktopJob("run-1"));
        var pull = (JobOffered)await server.PullJobAsync(new PullJobRequest("w1", null, DateTimeOffset.UtcNow));
        var leaseId = pull.Lease.LeaseId;
        var runId = pull.Lease.RunId;
        var attemptId = pull.Lease.AttemptId;

        // Worker cannot determine the outcome — submits UNKNOWN as final state
        await server.ProcessFinalResultAsync(new FinalResult(
            LeaseId: leaseId,
            WorkerId: "w1",
            Result: MakeRunResult(runId, attemptId, leaseId, "w1", RunState.Unknown)));

        var response = await server.ReconcileAsync(
            new ReconcileRequest(leaseId, "w1", null, null));
        response.Disposition.Should().Be(ReconcileDisposition.AlreadyTerminal,
            "UNKNOWN FinalResult still represents a terminal outcome");
        response.FinalRunState.Should().Be(RunState.Unknown,
            "the UNKNOWN terminal state must be preserved and returned in the reconcile response");
    }
}
