#nullable enable
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Application.Jobs;
using Tracto.Orchestrator.Domain.Journal;
using Tracto.Orchestrator.Domain.Leases;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Domain.Telemetry;
using Tracto.Orchestrator.Infrastructure.Clock;

namespace Tracto.Orchestrator.Unit.Tests.Jobs;

/// <summary>
/// Unit tests for AcceptLeaseCommandHandler - story OC-04.
/// Each test covers exactly one observable behaviour.
/// FakeClock controls time; all dependencies are Moq-backed (loose mocks).
///
/// Design note on AttemptId:
///   The LeaseRecord is created with the RunRecord's own AttemptId (set at run construction).
///   In the production flow PullJob transitions the run QUEUED→LEASED via AdoptLease, which
///   pins the lease's AttemptId as the new run-level fence. Tests that exercise the expired-
///   lease and idempotent paths therefore call run.AdoptLease(lease.AttemptId, ...) to mirror
///   that state transition faithfully.
///
/// Scenarios covered:
///   1. Valid accept → run RUNNING, AcceptedAt set, RUN_RUNNING journal entry, commit.
///   2. Wrong WorkerId → 409 WRONG_WORKER, rollback, no state changes.
///   3. Wrong AttemptId → 409 WRONG_ATTEMPT, rollback, no state changes.
///   4. Lease expired + no RUN_RUNNING journal → 409 LEASE_EXPIRED, run UNKNOWN.
///   5. Lease expired + RUN_RUNNING exists → 409 LEASE_EXPIRED, run re-queued.
///   6. Lease already Accepted (same worker + attempt) → idempotent 200, no new writes.
///   7. Lease payload contains only opaque secret refs, never plaintext values.
///   8. Background job pre-expired the lease (State=Expired) → 409 LEASE_EXPIRED, not 404.
/// </summary>
public sealed class AcceptLeaseCommandTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private const string WorkerId     = "worker-abc";
    private const string ArtifactId   = "artifact-001";
    private const string ArtifactVer  = "1.0.0";
    private const string ArtifactHash = "sha256-deadbeef";

    // Domain invariant 4: every SecretRef must begin with "ref:".
    private static readonly string[] SecretRefs = ["ref:vault/prod/api-key", "ref:vault/prod/db-pass"];
    private static readonly string[] InputRefs  = ["blob://run-input/data.json"];

    private static LeaseRecord MakeOfferedLease(Guid runId, Guid attemptId, string workerId)
        => new(
            leaseId:         Guid.NewGuid(),
            runId:           runId,
            attemptId:       attemptId,
            workerId:        workerId,
            artifactId:      ArtifactId,
            artifactVersion: ArtifactVer,
            artifactHash:    ArtifactHash,
            offeredAt:       FixedNow.AddSeconds(-10),
            expiresAt:       FixedNow.AddSeconds(20));

    private static LeaseRecord MakeExpiredLease(Guid runId, Guid attemptId, string workerId)
        => new(
            leaseId:         Guid.NewGuid(),
            runId:           runId,
            attemptId:       attemptId,
            workerId:        workerId,
            artifactId:      ArtifactId,
            artifactVersion: ArtifactVer,
            artifactHash:    ArtifactHash,
            offeredAt:       FixedNow.AddSeconds(-60),
            expiresAt:       FixedNow.AddSeconds(-30));

    private static RunRecord MakeQueuedRun(Guid runId)
    {
        var run = new RunRecord(
            id:               runId,
            workflowId:       "wf-test",
            createdAt:        FixedNow.AddMinutes(-5),
            correlationId:    Guid.NewGuid(),
            scriptArtifactId: ArtifactId,
            scriptVersion:    ArtifactVer,
            schemaVersion:    "1",
            inputRefs:        InputRefs,
            secretRefs:       SecretRefs,
            deadline:         null,
            eventId:          null,
            triggerType:      TriggerType.Manual,
            businessJobId:    null);
        run.Enqueue(FixedNow.AddMinutes(-4));
        return run;
    }

    private static Mock<ITelemetryService> MakeNullTelemetry()
    {
        var t = new Mock<ITelemetryService>();
        t.Setup(x => x.EmitAsync(It.IsAny<TelemetryEvent>(), It.IsAny<CancellationToken>()))
         .Returns(Task.CompletedTask);
        return t;
    }

    private static AcceptLeaseCommandHandler BuildHandler(
        Mock<IRunRepository>     runRepo,
        Mock<ILeaseRepository>   leaseRepo,
        Mock<IJournalRepository> journalRepo,
        Mock<IUnitOfWork>        uow)
        => new(
            runRepo.Object,
            leaseRepo.Object,
            journalRepo.Object,
            uow.Object,
            new FakeClock(FixedNow),
            MakeNullTelemetry().Object,
            NullLogger<AcceptLeaseCommandHandler>.Instance);

    [Fact]
    public async Task HandleAsync_ValidAccept_TransitionsRunToRunning_And_SetsAcceptedAt()
    {
        var runId = Guid.NewGuid();
        var run   = MakeQueuedRun(runId);
        var lease = MakeOfferedLease(runId, run.AttemptId, WorkerId);

        var runRepo     = new Mock<IRunRepository>();
        var leaseRepo   = new Mock<ILeaseRepository>();
        var journalRepo = new Mock<IJournalRepository>();
        var uow         = new Mock<IUnitOfWork>();

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);
        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var handler = BuildHandler(runRepo, leaseRepo, journalRepo, uow);
        var cmd     = new AcceptLeaseCommand(runId, run.AttemptId, WorkerId);

        var result = await handler.HandleAsync(cmd);

        result.IsSuccess.Should().BeTrue(because: "a valid accept must succeed");
        lease.State.Should().Be(LeaseState.Accepted);
        lease.AcceptedAt.Should().Be(FixedNow);
        run.State.Should().Be(RunState.Running,
            because: "the run must transition QUEUED->LEASED->RUNNING on a valid accept");
        journalRepo.Verify(
            r => r.AppendAsync(
                It.Is<JournalEntry>(j => j.EventType == "RUN_RUNNING" && j.RunId == runId),
                It.IsAny<CancellationToken>()),
            Times.Once);
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WrongWorkerId_Returns409_WRONG_WORKER()
    {
        var runId = Guid.NewGuid();
        var run   = MakeQueuedRun(runId);
        var lease = MakeOfferedLease(runId, run.AttemptId, "worker-real");

        var leaseRepo   = new Mock<ILeaseRepository>();
        var runRepo     = new Mock<IRunRepository>();
        var journalRepo = new Mock<IJournalRepository>();
        var uow         = new Mock<IUnitOfWork>();

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);

        var handler = BuildHandler(runRepo, leaseRepo, journalRepo, uow);
        var cmd     = new AcceptLeaseCommand(runId, run.AttemptId, "worker-impostor");

        var result = await handler.HandleAsync(cmd);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("WRONG_WORKER",
            because: "domain invariant 3 requires WorkerId mismatch -> 409 WRONG_WORKER");
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.RollbackAsync(), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WrongAttemptId_Returns409_WRONG_ATTEMPT()
    {
        var runId        = Guid.NewGuid();
        var run          = MakeQueuedRun(runId);
        var wrongAttempt = Guid.NewGuid();
        var lease        = MakeOfferedLease(runId, run.AttemptId, WorkerId);

        var leaseRepo   = new Mock<ILeaseRepository>();
        var runRepo     = new Mock<IRunRepository>();
        var journalRepo = new Mock<IJournalRepository>();
        var uow         = new Mock<IUnitOfWork>();

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);

        var handler = BuildHandler(runRepo, leaseRepo, journalRepo, uow);
        var cmd     = new AcceptLeaseCommand(runId, wrongAttempt, WorkerId);

        var result = await handler.HandleAsync(cmd);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("WRONG_ATTEMPT",
            because: "domain invariant 3 requires stale AttemptId -> 409 WRONG_ATTEMPT");
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.RollbackAsync(), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ExpiredLease_NoRunningEvidence_MarksRunUnknown()
    {
        var runId = Guid.NewGuid();
        var run   = MakeQueuedRun(runId);
        var lease = MakeExpiredLease(runId, run.AttemptId, WorkerId);

        // Precondition: PullJob already transitioned the run QUEUED→LEASED (via AdoptLease)
        // before the lease expired. Mirror that state here.
        run.AdoptLease(lease.AttemptId, WorkerId, FixedNow.AddMinutes(-2));

        var leaseRepo   = new Mock<ILeaseRepository>();
        var runRepo     = new Mock<IRunRepository>();
        var journalRepo = new Mock<IJournalRepository>();
        var uow         = new Mock<IUnitOfWork>();

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);
        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        journalRepo.Setup(r => r.HasEntriesOfTypeAsync(runId, "RUN_RUNNING", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(false);

        var handler = BuildHandler(runRepo, leaseRepo, journalRepo, uow);
        var cmd     = new AcceptLeaseCommand(runId, run.AttemptId, WorkerId);

        var result = await handler.HandleAsync(cmd);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("LEASE_EXPIRED");
        run.State.Should().Be(RunState.Unknown,
            because: "start is uncertain with no RUN_RUNNING evidence -> UNKNOWN (invariant 2)");
        run.NeedsAttention.Should().BeTrue(
            because: "domain invariant 2: UNKNOWN always requires NeedsAttention=true");
        run.UnknownReason.Should().NotBeNullOrWhiteSpace(
            because: "domain invariant 2: UNKNOWN always requires a non-empty UnknownReason");
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ExpiredLease_WithRunningEvidence_RequeuesRun()
    {
        var runId = Guid.NewGuid();
        var run   = MakeQueuedRun(runId);
        var lease = MakeExpiredLease(runId, run.AttemptId, WorkerId);

        // Precondition: PullJob already transitioned the run QUEUED→LEASED (via AdoptLease)
        // before the lease expired. Mirror that state here.
        // Requeue() (used in the handler's expired+evidence path) requires LEASED state.
        run.AdoptLease(lease.AttemptId, WorkerId, FixedNow.AddMinutes(-2));

        var leaseRepo   = new Mock<ILeaseRepository>();
        var runRepo     = new Mock<IRunRepository>();
        var journalRepo = new Mock<IJournalRepository>();
        var uow         = new Mock<IUnitOfWork>();

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);
        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        journalRepo.Setup(r => r.HasEntriesOfTypeAsync(runId, "RUN_RUNNING", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(true);

        var handler = BuildHandler(runRepo, leaseRepo, journalRepo, uow);
        var cmd     = new AcceptLeaseCommand(runId, run.AttemptId, WorkerId);

        var result = await handler.HandleAsync(cmd);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("LEASE_EXPIRED");
        run.State.Should().Be(RunState.Queued,
            because: "RUN_RUNNING evidence confirms start -> safe to requeue for retry");
        run.NeedsAttention.Should().BeFalse(
            because: "a requeued run does not require human review");
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_SameWorkerAndAttempt_LeaseAlreadyAccepted_Returns200_Idempotent()
    {
        var runId = Guid.NewGuid();
        var run   = MakeQueuedRun(runId);
        var lease = MakeOfferedLease(runId, run.AttemptId, WorkerId);

        // Mirror the production AcceptLease success flow: AdoptLease (QUEUED→LEASED) then Start.
        run.AdoptLease(lease.AttemptId, WorkerId, FixedNow.AddSeconds(-5));
        run.Start(lease.AttemptId, FixedNow.AddSeconds(-5));
        lease.Accept(FixedNow.AddSeconds(-5));

        var leaseRepo   = new Mock<ILeaseRepository>();
        var runRepo     = new Mock<IRunRepository>();
        var journalRepo = new Mock<IJournalRepository>();
        var uow         = new Mock<IUnitOfWork>();

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);
        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var handler = BuildHandler(runRepo, leaseRepo, journalRepo, uow);
        var cmd     = new AcceptLeaseCommand(runId, run.AttemptId, WorkerId);

        var result = await handler.HandleAsync(cmd);

        result.IsSuccess.Should().BeTrue(
            because: "duplicate AcceptLease with same worker+attempt must be idempotent 200");
        result.Value.RunId.Should().Be(runId);
        result.Value.AttemptId.Should().Be(run.AttemptId);
        journalRepo.Verify(
            r => r.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "no journal entry on idempotent repeat");
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never,
            "CommitAsync must not be called when the request is idempotent");
    }

    [Fact]
    public async Task HandleAsync_ValidAccept_LeasePayload_ContainsNoPlaintextSecrets()
    {
        var runId = Guid.NewGuid();
        var run   = MakeQueuedRun(runId);
        var lease = MakeOfferedLease(runId, run.AttemptId, WorkerId);

        var leaseRepo   = new Mock<ILeaseRepository>();
        var runRepo     = new Mock<IRunRepository>();
        var journalRepo = new Mock<IJournalRepository>();
        var uow         = new Mock<IUnitOfWork>();

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);
        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var handler = BuildHandler(runRepo, leaseRepo, journalRepo, uow);
        var cmd     = new AcceptLeaseCommand(runId, run.AttemptId, WorkerId);

        var result = await handler.HandleAsync(cmd);

        result.IsSuccess.Should().BeTrue();
        var payload = result.Value;

        payload.RunId.Should().Be(runId);
        payload.AttemptId.Should().Be(run.AttemptId);
        payload.ArtifactId.Should().Be(ArtifactId);
        payload.ArtifactVersion.Should().Be(ArtifactVer);
        payload.ArtifactHash.Should().Be(ArtifactHash);
        payload.InputRefs.Should().BeEquivalentTo(InputRefs);
        payload.SecretRefs.Should().BeEquivalentTo(SecretRefs,
            because: "SecretRefs must be opaque references, never resolved plaintext values (invariant 4)");

        // Domain invariant 4: every SecretRef must be an opaque "ref:" reference.
        foreach (var secretRef in payload.SecretRefs)
        {
            secretRef.Should().StartWith("ref:",
                because: "domain invariant 4 requires every SecretRef to begin with 'ref:'");
        }
    }

    /// <summary>
    /// Covers the gap where the background expiry job explicitly marks a lease Expired
    /// (State=Expired) before the worker's AcceptLease arrives.
    /// FindActiveLeaseForRunAsync filters out Expired leases, so the handler must fall back
    /// to FindByRunIdAsync and return 409 LEASE_EXPIRED instead of 404 NotFound.
    /// </summary>
    [Fact]
    public async Task HandleAsync_BackgroundExpiredLease_Returns409_LEASE_EXPIRED_Not404()
    {
        var runId = Guid.NewGuid();
        var run   = MakeQueuedRun(runId);
        // Simulate: background expiry job already called Expire() on the lease.
        var expiredLease = MakeExpiredLease(runId, run.AttemptId, WorkerId);
        expiredLease.Expire(); // background job ran first; State is now Expired

        var leaseRepo   = new Mock<ILeaseRepository>();
        var runRepo     = new Mock<IRunRepository>();
        var journalRepo = new Mock<IJournalRepository>();
        var uow         = new Mock<IUnitOfWork>();

        // FindActiveLeaseForRunAsync returns null (filters State != Expired).
        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((LeaseRecord?)null);
        // FindByRunIdAsync returns the explicitly-expired lease (any state).
        leaseRepo.Setup(r => r.FindByRunIdAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(expiredLease);

        var handler = BuildHandler(runRepo, leaseRepo, journalRepo, uow);
        var cmd     = new AcceptLeaseCommand(runId, run.AttemptId, WorkerId);

        var result = await handler.HandleAsync(cmd);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("LEASE_EXPIRED",
            because: "a background-expired lease must return 409 LEASE_EXPIRED, " +
                     "not 404 NotFound — the worker needs a precise error code to decide whether to retry");
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never,
            "no commit when returning a terminal error on the background-expired path");
    }
}
