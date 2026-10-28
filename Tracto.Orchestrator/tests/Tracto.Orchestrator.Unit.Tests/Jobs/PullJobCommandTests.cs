#nullable enable
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Application.Jobs;
using Tracto.Orchestrator.Domain.Artifacts;
using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Journal;
using Tracto.Orchestrator.Domain.Leases;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Domain.Workers;
using Tracto.Orchestrator.Infrastructure.Clock;

namespace Tracto.Orchestrator.Unit.Tests.Jobs;

/// <summary>
/// Unit tests for <see cref="PullJobCommandHandler"/> — story OC-03.
/// Each test covers exactly one observable behaviour.
/// FakeClock controls time; all external dependencies are Moq-backed.
///
/// Handler algorithm under test:
///   1. Validate WorkerId + RuntimeType.
///   2. Worker must exist and be AVAILABLE.
///   3. Worker must not already hold an active lease.
///   4. Find first QUEUED run whose artifact RuntimeType + required capabilities match.
///   5. Run must not already be offered to another worker.
///   6. If headed: session exclusivity must hold.
///   7. On match: create LeaseRecord (Offered, ExpiresAt = now+30s, AttemptId = new Guid),
///      emit LEASE_OFFERED journal entry, commit, return JobOffered.
///   8. No match → NoPendingJob (no writes).
/// </summary>
public sealed class PullJobCommandTests
{
    // ── Fixed test baseline ───────────────────────────────────────────────────

    private static readonly DateTimeOffset FixedNow =
        new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private const string WorkerId       = "worker-abc";
    private const string RuntimeType    = "dotnet";
    private const string ArtifactId     = "script-artifact-001";
    private const string ArtifactVersion = "1.0.0";
    private const string ContentHash    = "sha256-deadbeef";

    // SelectorContent fragments used throughout tests.
    private const string MatchingSelector =
        """{"runtimeType":"dotnet","requiredCapabilities":[],"headed":false}""";

    private const string WrongRuntimeSelector =
        """{"runtimeType":"browser","requiredCapabilities":[],"headed":false}""";

    private static string CapabilitySelector(params string[] caps)
    {
        var capsJson = string.Join(",", caps.Select(c => $"\"{c}\""));
        return $$"""{"runtimeType":"dotnet","requiredCapabilities":[{{capsJson}}],"headed":false}""";
    }

    private const string HeadedSelector =
        """{"runtimeType":"dotnet","requiredCapabilities":[],"headed":true}""";

    // ── Infrastructure helpers ─────────────────────────────────────────────────

    /// <summary>Builds a fresh AVAILABLE <see cref="WorkerRecord"/> with the given capabilities.</summary>
    private static WorkerRecord MakeAvailableWorker(
        string   workerId      = WorkerId,
        string   runtimeType   = RuntimeType,
        string[]? capabilities = null)
    {
        var cap = capabilities is not null
            ? new WorkerCapability(capabilities, runtimeType)
            : new WorkerCapability(["ClickAction", "TypeAction"], runtimeType);

        return new WorkerRecord(workerId, runtimeType, 120, FixedNow.AddMinutes(-5), cap);
    }

    /// <summary>Returns a QUEUED <see cref="RunRecord"/> referencing the test artifact.</summary>
    private static RunRecord MakeQueuedRun(
        string artifactId      = ArtifactId,
        string artifactVersion = ArtifactVersion)
    {
        var run = new RunRecord(
            id:               Guid.NewGuid(),
            workflowId:       artifactId,
            createdAt:        FixedNow.AddMinutes(-1),
            correlationId:    Guid.NewGuid(),
            scriptArtifactId: artifactId,
            scriptVersion:    artifactVersion,
            schemaVersion:    "1",
            inputRefs:        ["input://blob/a", "input://blob/b"],
            secretRefs:       ["secret://vault/key-1"],
            deadline:         null,
            eventId:          null,
            triggerType:      TriggerType.Manual,
            businessJobId:    null);

        run.Enqueue(FixedNow.AddMinutes(-1));
        return run;
    }

    /// <summary>Builds a non-deleted <see cref="ArtifactRecord"/> with the given selector.</summary>
    private static ArtifactRecord MakeArtifact(string? selectorContent = MatchingSelector) =>
        new(ArtifactId, ArtifactVersion, "1", ContentHash,
            FixedNow.AddDays(-1), signatureValid: true,
            selectorContent: selectorContent);

    /// <summary>
    /// Wires up a fully-configured handler backed by Moq mocks.
    /// Happy-path defaults let individual tests override only what they care about.
    /// </summary>
    private static PullJobCommandHandler MakeHandler(
        out Mock<IWorkerRepository>   workerRepo,
        out Mock<IRunRepository>      runRepo,
        out Mock<IArtifactRepository> artifactRepo,
        out Mock<ILeaseRepository>    leaseRepo,
        out Mock<IJournalRepository>  journalRepo,
        out Mock<IUnitOfWork>         unitOfWork,
        FakeClock? clock = null)
    {
        workerRepo   = new Mock<IWorkerRepository>(MockBehavior.Strict);
        runRepo      = new Mock<IRunRepository>(MockBehavior.Strict);
        artifactRepo = new Mock<IArtifactRepository>(MockBehavior.Strict);
        leaseRepo    = new Mock<ILeaseRepository>(MockBehavior.Strict);
        journalRepo  = new Mock<IJournalRepository>(MockBehavior.Strict);
        unitOfWork   = new Mock<IUnitOfWork>(MockBehavior.Strict);

        // UnitOfWork lifecycle — always succeeds by default.
        unitOfWork.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        // RollbackAsync is called by the finally block on every non-committed exit path
        // (NoPendingJob, error returns, and exceptions). Documented as safe to call on an
        // empty or already-committed transaction.
        unitOfWork.Setup(u => u.RollbackAsync())
                  .Returns(Task.CompletedTask);

        // Journal writes — never fail.
        journalRepo.Setup(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
                   .Returns(Task.CompletedTask);

        // Lease writes — never fail.
        leaseRepo.Setup(l => l.AddAsync(It.IsAny<LeaseRecord>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        return new PullJobCommandHandler(
            runRepo.Object,
            workerRepo.Object,
            artifactRepo.Object,
            leaseRepo.Object,
            journalRepo.Object,
            unitOfWork.Object,
            clock ?? new FakeClock(FixedNow),
            NullLogger<PullJobCommandHandler>.Instance);
    }

    /// <summary>Configures the mocks for the standard happy-path scenario.</summary>
    private static void SetupHappyPath(
        Mock<IWorkerRepository>   workerRepo,
        Mock<IRunRepository>      runRepo,
        Mock<IArtifactRepository> artifactRepo,
        Mock<ILeaseRepository>    leaseRepo,
        RunRecord?    run      = null,
        ArtifactRecord? artifact = null,
        string workerId          = WorkerId,
        string? interactiveSessionId = null)
    {
        var theRun      = run      ?? MakeQueuedRun();
        var theArtifact = artifact ?? MakeArtifact();

        workerRepo.Setup(w => w.GetByIdAsync(workerId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MakeAvailableWorker(workerId));

        leaseRepo.Setup(l => l.FindActiveForWorkerAsync(workerId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((LeaseRecord?)null);

        runRepo.Setup(r => r.FindByStateAsync(RunState.Queued, It.IsAny<CancellationToken>()))
               .ReturnsAsync((IReadOnlyList<RunRecord>) new List<RunRecord> { theRun });

        artifactRepo.Setup(a => a.GetRecordAsync(
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(theArtifact);

        leaseRepo.Setup(l => l.HasActiveOfferForRunAsync(theRun.Id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false);

        if (interactiveSessionId is not null)
        {
            leaseRepo.Setup(l => l.HasActiveSessionLeaseAsync(
                        interactiveSessionId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(false);
        }
    }

    // ── 1. Capable worker → JobOffered ────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_CapableWorker_ReturnsJobOffered()
    {
        // Arrange
        var handler = MakeHandler(
            out var workerRepo, out var runRepo,
            out var artifactRepo, out var leaseRepo,
            out _, out _);

        var run = MakeQueuedRun();
        SetupHappyPath(workerRepo, runRepo, artifactRepo, leaseRepo, run: run);

        var cmd = new PullJobCommand(WorkerId, RuntimeType, ["ClickAction", "TypeAction"]);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue(because: "a capable, available worker must receive a job offer");
        result.Value.Should().BeOfType<MatchResult.JobOffered>(
            because: "the result must be a JobOffered discriminated-union case");

        var offered = (MatchResult.JobOffered) result.Value;
        offered.RunId.Should().Be(run.Id,
            because: "the offered RunId must match the queued run");
        offered.ArtifactId.Should().Be(ArtifactId,
            because: "artifact identifier must be forwarded to the worker");
        offered.ArtifactVersion.Should().Be(ArtifactVersion,
            because: "artifact version must be forwarded to the worker");
        offered.ArtifactHash.Should().Be(ContentHash,
            because: "the pinned content hash guards against tampered artifacts");
        offered.InputRefs.Should().BeEquivalentTo(["input://blob/a", "input://blob/b"],
            because: "InputRefs must be forwarded verbatim — no secret values stored (invariant 4)");
        offered.SecretRefs.Should().BeEquivalentTo(["secret://vault/key-1"],
            because: "SecretRefs are opaque references — no plaintext secrets returned");
        offered.AttemptId.Should().NotBeEmpty(
            because: "a new AttemptId must fence subsequent stage transitions");
    }

    [Fact]
    public async Task HandleAsync_CapableWorker_LeaseCreatedWithCorrectExpiry()
    {
        // Arrange
        var clock   = new FakeClock(FixedNow);
        var handler = MakeHandler(
            out var workerRepo, out var runRepo,
            out var artifactRepo, out var leaseRepo,
            out _, out _, clock: clock);

        SetupHappyPath(workerRepo, runRepo, artifactRepo, leaseRepo);

        LeaseRecord? capturedLease = null;
        leaseRepo.Setup(l => l.AddAsync(It.IsAny<LeaseRecord>(), It.IsAny<CancellationToken>()))
                 .Callback<LeaseRecord, CancellationToken>((l, _) => capturedLease = l)
                 .Returns(Task.CompletedTask);

        var cmd = new PullJobCommand(WorkerId, RuntimeType, ["ClickAction"]);

        // Act
        await handler.HandleAsync(cmd);

        // Assert
        capturedLease.Should().NotBeNull(because: "AddAsync must have been called with the new lease");
        capturedLease!.State.Should().Be(LeaseState.Offered,
            because: "a freshly created lease starts in the Offered state");
        capturedLease.ExpiresAt.Should().Be(FixedNow + PullJobCommandHandler.LeaseOfferWindow,
            because: "ExpiresAt must be exactly now + 30 seconds (spec invariant)");
        capturedLease.WorkerId.Should().Be(WorkerId,
            because: "the lease must identify the requesting worker");
        capturedLease.AttemptId.Should().NotBeEmpty(
            because: "each lease gets a fresh AttemptId for fencing subsequent calls");
        capturedLease.OfferedAt.Should().Be(FixedNow,
            because: "OfferedAt must record the exact offer timestamp from IClock");
    }

    [Fact]
    public async Task HandleAsync_CapableWorker_LeaseOfferWindowIsThirtySeconds()
    {
        // Arrange / Act / Assert
        PullJobCommandHandler.LeaseOfferWindow
            .Should().Be(TimeSpan.FromSeconds(30),
                because: "the spec mandates a 30-second acceptance window");
    }

    [Fact]
    public async Task HandleAsync_CapableWorker_JournalEntryLeaseOfferedEmitted()
    {
        // Arrange
        var handler = MakeHandler(
            out var workerRepo, out var runRepo,
            out var artifactRepo, out var leaseRepo,
            out var journalRepo, out _);

        SetupHappyPath(workerRepo, runRepo, artifactRepo, leaseRepo);

        var cmd = new PullJobCommand(WorkerId, RuntimeType, ["ClickAction"]);

        // Act
        await handler.HandleAsync(cmd);

        // Assert — exactly one LEASE_OFFERED entry must be written.
        journalRepo.Verify(
            j => j.AppendAsync(
                It.Is<JournalEntry>(e => e.EventType == "LEASE_OFFERED"),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "a LEASE_OFFERED journal entry must be appended atomically with the lease (invariant 5)");
    }

    [Fact]
    public async Task HandleAsync_CapableWorker_CommitsUnitOfWork()
    {
        // Arrange
        var handler = MakeHandler(
            out var workerRepo, out var runRepo,
            out var artifactRepo, out var leaseRepo,
            out _, out var uow);

        SetupHappyPath(workerRepo, runRepo, artifactRepo, leaseRepo);

        var cmd = new PullJobCommand(WorkerId, RuntimeType, ["ClickAction"]);

        // Act
        await handler.HandleAsync(cmd);

        // Assert
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once,
            "CommitAsync must be called exactly once to satisfy domain invariant 5 (atomic writes)");
    }

    // ── 2. Wrong RuntimeType → NoPendingJob ───────────────────────────────────

    [Fact]
    public async Task HandleAsync_WrongRuntimeType_ReturnsNoPendingJob()
    {
        // Arrange
        var handler = MakeHandler(
            out var workerRepo, out var runRepo,
            out var artifactRepo, out var leaseRepo,
            out _, out _);

        var run      = MakeQueuedRun();
        var artifact = MakeArtifact(selectorContent: WrongRuntimeSelector); // "browser"

        workerRepo.Setup(w => w.GetByIdAsync(WorkerId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MakeAvailableWorker(runtimeType: "dotnet")); // worker is "dotnet"

        leaseRepo.Setup(l => l.FindActiveForWorkerAsync(WorkerId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((LeaseRecord?)null);

        runRepo.Setup(r => r.FindByStateAsync(RunState.Queued, It.IsAny<CancellationToken>()))
               .ReturnsAsync((IReadOnlyList<RunRecord>) new List<RunRecord> { run });

        artifactRepo.Setup(a => a.GetRecordAsync(
                        ArtifactId, ArtifactVersion, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(artifact); // artifact requires "browser" runtime

        leaseRepo.Setup(l => l.HasActiveOfferForRunAsync(run.Id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false);

        // Worker runtime is "dotnet" but artifact requires "browser".
        var cmd = new PullJobCommand(WorkerId, "dotnet", ["ClickAction"]);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<MatchResult.NoPendingJob>(
            because: "no run matches when the artifact's runtimeType differs from the worker's");
    }

    [Fact]
    public async Task HandleAsync_WrongRuntimeType_NoLeaseCreated()
    {
        // Arrange
        var handler = MakeHandler(
            out var workerRepo, out var runRepo,
            out var artifactRepo, out var leaseRepo,
            out var journalRepo, out var uow);

        var run      = MakeQueuedRun();
        var artifact = MakeArtifact(selectorContent: WrongRuntimeSelector);

        workerRepo.Setup(w => w.GetByIdAsync(WorkerId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MakeAvailableWorker());
        leaseRepo.Setup(l => l.FindActiveForWorkerAsync(WorkerId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((LeaseRecord?)null);
        runRepo.Setup(r => r.FindByStateAsync(RunState.Queued, It.IsAny<CancellationToken>()))
               .ReturnsAsync((IReadOnlyList<RunRecord>) new List<RunRecord> { run });
        artifactRepo.Setup(a => a.GetRecordAsync(
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(artifact);
        leaseRepo.Setup(l => l.HasActiveOfferForRunAsync(run.Id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false);

        var cmd = new PullJobCommand(WorkerId, "dotnet", ["ClickAction"]);

        // Act
        await handler.HandleAsync(cmd);

        // Assert — no lease, no journal entry on a non-match.
        leaseRepo.Verify(l => l.AddAsync(It.IsAny<LeaseRecord>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "no lease must be created when no matching run is found");
        journalRepo.Verify(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "no journal entry must be written when no matching run is found");
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Never,
            "CommitAsync must not be called when there is nothing to persist");
    }

    // ── 3. Worker already has active lease → NoPendingJob ─────────────────────

    [Fact]
    public async Task HandleAsync_WorkerAlreadyHasActiveLease_ReturnsNoPendingJob()
    {
        // Arrange
        var handler = MakeHandler(
            out var workerRepo, out _, out _, out var leaseRepo,
            out _, out _);

        var existingLease = new LeaseRecord(
            leaseId:        Guid.NewGuid(),
            runId:          Guid.NewGuid(),
            attemptId:      Guid.NewGuid(),
            workerId:       WorkerId,
            artifactId:     ArtifactId,
            artifactVersion: ArtifactVersion,
            artifactHash:   ContentHash,
            offeredAt:      FixedNow.AddSeconds(-10),
            expiresAt:      FixedNow.AddSeconds(20));   // still active

        workerRepo.Setup(w => w.GetByIdAsync(WorkerId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MakeAvailableWorker());

        leaseRepo.Setup(l => l.FindActiveForWorkerAsync(WorkerId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(existingLease);   // ← worker holds a lease already

        var cmd = new PullJobCommand(WorkerId, RuntimeType, ["ClickAction"]);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<MatchResult.NoPendingJob>(
            because: "a worker already holding an active lease must not receive a second offer");
    }

    [Fact]
    public async Task HandleAsync_WorkerAlreadyHasActiveLease_NoRunQueryMade()
    {
        // Arrange
        var handler = MakeHandler(
            out var workerRepo, out var runRepo,
            out _, out var leaseRepo, out _, out _);

        var existingLease = new LeaseRecord(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            WorkerId, ArtifactId, ArtifactVersion, ContentHash,
            FixedNow.AddSeconds(-5), FixedNow.AddSeconds(25));

        workerRepo.Setup(w => w.GetByIdAsync(WorkerId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MakeAvailableWorker());
        leaseRepo.Setup(l => l.FindActiveForWorkerAsync(WorkerId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(existingLease);

        var cmd = new PullJobCommand(WorkerId, RuntimeType, []);

        // Act
        await handler.HandleAsync(cmd);

        // Assert — the handler must short-circuit before querying runs.
        runRepo.Verify(r => r.FindByStateAsync(It.IsAny<RunState>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "FindByStateAsync must not be called when the worker already holds an active lease");
    }

    // ── 4. Two concurrent PullJob → exactly one JobOffered (transaction test) ──

    [Fact]
    public async Task HandleAsync_ConcurrentPullJob_SecondCallReturnedNoPendingJobAfterFirstSucceeds()
    {
        // Arrange — simulate what happens in a serializable transaction:
        // first call creates a lease; second call sees the run is already offered.
        var clock   = new FakeClock(FixedNow);
        var handler = MakeHandler(
            out var workerRepo, out var runRepo,
            out var artifactRepo, out var leaseRepo,
            out _, out _,
            clock: clock);

        var run      = MakeQueuedRun();
        var artifact = MakeArtifact();

        workerRepo.Setup(w => w.GetByIdAsync(WorkerId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MakeAvailableWorker());
        leaseRepo.Setup(l => l.FindActiveForWorkerAsync(WorkerId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((LeaseRecord?)null);
        runRepo.Setup(r => r.FindByStateAsync(RunState.Queued, It.IsAny<CancellationToken>()))
               .ReturnsAsync((IReadOnlyList<RunRecord>) new List<RunRecord> { run });
        artifactRepo.Setup(a => a.GetRecordAsync(
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(artifact);

        // First call: run is free → JobOffered.
        leaseRepo.SetupSequence(l => l.HasActiveOfferForRunAsync(run.Id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false)   // first  call — no competing offer yet
                 .ReturnsAsync(true);   // second call — first worker already holds it

        var cmd = new PullJobCommand(WorkerId, RuntimeType, ["ClickAction"]);

        // Act — first call
        var first  = await handler.HandleAsync(cmd);

        // Second call: the same run is now offered to the first worker.
        // (In production, the serializable DB transaction prevents both from seeing false.)
        var second = await handler.HandleAsync(cmd);

        // Assert
        first.IsSuccess.Should().BeTrue();
        first.Value.Should().BeOfType<MatchResult.JobOffered>(
            because: "the first pull must succeed when the run is free");

        second.IsSuccess.Should().BeTrue();
        second.Value.Should().BeOfType<MatchResult.NoPendingJob>(
            because: "the second pull must get NoPendingJob when the run is already offered");
    }

    [Fact]
    public async Task HandleAsync_ConcurrentPullJob_UnitOfWorkBeginsBeforeAnyWrite()
    {
        // Arrange — verify that BeginAsync wraps all read-and-write steps.
        var callOrder = new List<string>();

        var handler = MakeHandler(
            out var workerRepo, out var runRepo,
            out var artifactRepo, out var leaseRepo,
            out _, out var uow);

        SetupHappyPath(workerRepo, runRepo, artifactRepo, leaseRepo);

        uow.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>()))
           .Callback(() => callOrder.Add("Begin"))
           .Returns(Task.CompletedTask);

        leaseRepo.Setup(l => l.AddAsync(It.IsAny<LeaseRecord>(), It.IsAny<CancellationToken>()))
                 .Callback(() => callOrder.Add("AddLease"))
                 .Returns(Task.CompletedTask);

        uow.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
           .Callback(() => callOrder.Add("Commit"))
           .Returns(Task.CompletedTask);

        var cmd = new PullJobCommand(WorkerId, RuntimeType, ["ClickAction"]);

        // Act
        await handler.HandleAsync(cmd);

        // Assert — Begin must appear before any write and Commit must close the sequence.
        callOrder.Should().Contain("Begin");
        callOrder.Should().Contain("AddLease");
        callOrder.Should().Contain("Commit");

        var beginIdx  = callOrder.IndexOf("Begin");
        var leaseIdx  = callOrder.IndexOf("AddLease");
        var commitIdx = callOrder.IndexOf("Commit");

        beginIdx.Should().BeLessThan(leaseIdx,
            because: "the transaction must be opened before the lease is written");
        leaseIdx.Should().BeLessThan(commitIdx,
            because: "the lease must be written before the transaction is committed");
    }

    // ── 5. InteractiveSessionId exclusivity → second headed worker blocked ─────

    [Fact]
    public async Task HandleAsync_HeadedWorker_WhenSessionAlreadyHeld_ReturnsNoPendingJob()
    {
        // Arrange
        const string sessionId = "session-desktop-1";

        var handler = MakeHandler(
            out var workerRepo, out var runRepo,
            out var artifactRepo, out var leaseRepo,
            out _, out _);

        var run      = MakeQueuedRun();
        var artifact = MakeArtifact(selectorContent: HeadedSelector);

        workerRepo.Setup(w => w.GetByIdAsync(WorkerId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MakeAvailableWorker());
        leaseRepo.Setup(l => l.FindActiveForWorkerAsync(WorkerId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((LeaseRecord?)null);
        runRepo.Setup(r => r.FindByStateAsync(RunState.Queued, It.IsAny<CancellationToken>()))
               .ReturnsAsync((IReadOnlyList<RunRecord>) new List<RunRecord> { run });
        artifactRepo.Setup(a => a.GetRecordAsync(
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(artifact);
        leaseRepo.Setup(l => l.HasActiveOfferForRunAsync(run.Id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false);

        // Another headed worker is already using session-desktop-1.
        leaseRepo.Setup(l => l.HasActiveSessionLeaseAsync(sessionId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);

        var cmd = new PullJobCommand(WorkerId, RuntimeType, ["ClickAction"],
            InteractiveSessionId: sessionId);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert — foreground-input exclusivity: second headed worker must be blocked.
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<MatchResult.NoPendingJob>(
            because: "only one headed worker may hold the foreground-input lease per session (invariant 6)");
    }

    [Fact]
    public async Task HandleAsync_HeadedWorker_WhenSessionIsFree_ReturnsJobOffered()
    {
        // Arrange — session is free, so the headed worker should receive the offer.
        const string sessionId = "session-free";

        var handler = MakeHandler(
            out var workerRepo, out var runRepo,
            out var artifactRepo, out var leaseRepo,
            out _, out _);

        var run      = MakeQueuedRun();
        var artifact = MakeArtifact(selectorContent: HeadedSelector);

        SetupHappyPath(workerRepo, runRepo, artifactRepo, leaseRepo,
            run: run, artifact: artifact,
            interactiveSessionId: sessionId);

        var cmd = new PullJobCommand(WorkerId, RuntimeType, ["ClickAction"],
            InteractiveSessionId: sessionId);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<MatchResult.JobOffered>(
            because: "a headed worker should receive a job when no other worker holds the session");
    }

    [Fact]
    public async Task HandleAsync_HeadedWorker_LeaseCarriesInteractiveSessionId()
    {
        // Arrange
        const string sessionId = "session-xyz";

        var handler = MakeHandler(
            out var workerRepo, out var runRepo,
            out var artifactRepo, out var leaseRepo,
            out _, out _);

        var run      = MakeQueuedRun();
        var artifact = MakeArtifact(selectorContent: HeadedSelector);

        SetupHappyPath(workerRepo, runRepo, artifactRepo, leaseRepo,
            run: run, artifact: artifact,
            interactiveSessionId: sessionId);

        LeaseRecord? capturedLease = null;
        leaseRepo.Setup(l => l.AddAsync(It.IsAny<LeaseRecord>(), It.IsAny<CancellationToken>()))
                 .Callback<LeaseRecord, CancellationToken>((l, _) => capturedLease = l)
                 .Returns(Task.CompletedTask);

        var cmd = new PullJobCommand(WorkerId, RuntimeType, ["ClickAction"],
            InteractiveSessionId: sessionId);

        // Act
        await handler.HandleAsync(cmd);

        // Assert — the session ID must be threaded through to the lease.
        capturedLease.Should().NotBeNull();
        capturedLease!.InteractiveSessionId.Should().Be(sessionId,
            because: "the lease must carry the InteractiveSessionId so future exclusivity checks can use it");
    }

    // ── 6. Wrong capability name → NoPendingJob ───────────────────────────────

    [Fact]
    public async Task HandleAsync_WorkerMissingRequiredCapability_ReturnsNoPendingJob()
    {
        // Arrange — job requires "ClickAction"; worker only has "TypeAction".
        var handler = MakeHandler(
            out var workerRepo, out var runRepo,
            out var artifactRepo, out var leaseRepo,
            out _, out _);

        var run      = MakeQueuedRun();
        var artifact = MakeArtifact(
            selectorContent: CapabilitySelector("ClickAction"));  // requires ClickAction

        workerRepo.Setup(w => w.GetByIdAsync(WorkerId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MakeAvailableWorker(capabilities: ["TypeAction"]));  // lacks ClickAction

        leaseRepo.Setup(l => l.FindActiveForWorkerAsync(WorkerId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((LeaseRecord?)null);
        runRepo.Setup(r => r.FindByStateAsync(RunState.Queued, It.IsAny<CancellationToken>()))
               .ReturnsAsync((IReadOnlyList<RunRecord>) new List<RunRecord> { run });
        artifactRepo.Setup(a => a.GetRecordAsync(
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(artifact);
        leaseRepo.Setup(l => l.HasActiveOfferForRunAsync(run.Id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false);

        var cmd = new PullJobCommand(WorkerId, RuntimeType, ["TypeAction"]); // no ClickAction

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<MatchResult.NoPendingJob>(
            because: "required capabilities must be a subset of worker capabilities; " +
                     "a worker missing even one required capability must be skipped");
    }

    [Fact]
    public async Task HandleAsync_WorkerHasAllRequiredCapabilities_ReturnsJobOffered()
    {
        // Arrange — job requires ClickAction + TypeAction; worker has both.
        var handler = MakeHandler(
            out var workerRepo, out var runRepo,
            out var artifactRepo, out var leaseRepo,
            out _, out _);

        var run      = MakeQueuedRun();
        var artifact = MakeArtifact(
            selectorContent: CapabilitySelector("ClickAction", "TypeAction"));

        workerRepo.Setup(w => w.GetByIdAsync(WorkerId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MakeAvailableWorker(capabilities: ["ClickAction", "TypeAction", "ExtraAction"]));

        leaseRepo.Setup(l => l.FindActiveForWorkerAsync(WorkerId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((LeaseRecord?)null);
        runRepo.Setup(r => r.FindByStateAsync(RunState.Queued, It.IsAny<CancellationToken>()))
               .ReturnsAsync((IReadOnlyList<RunRecord>) new List<RunRecord> { run });
        artifactRepo.Setup(a => a.GetRecordAsync(
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(artifact);
        leaseRepo.Setup(l => l.HasActiveOfferForRunAsync(run.Id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false);

        var cmd = new PullJobCommand(WorkerId, RuntimeType,
            ["ClickAction", "TypeAction", "ExtraAction"]);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<MatchResult.JobOffered>(
            because: "when required capabilities are a strict subset of worker capabilities, " +
                     "the worker is eligible for the job");
    }

    [Fact]
    public async Task HandleAsync_ArtifactHasNoRequiredCapabilities_AnyWorkerIsEligible()
    {
        // Arrange — artifact has no required capabilities (wildcard).
        var handler = MakeHandler(
            out var workerRepo, out var runRepo,
            out var artifactRepo, out var leaseRepo,
            out _, out _);

        // Worker has no special capabilities.
        workerRepo.Setup(w => w.GetByIdAsync(WorkerId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MakeAvailableWorker(capabilities: []));

        var run      = MakeQueuedRun();
        var artifact = MakeArtifact(selectorContent: MatchingSelector); // requiredCapabilities:[]

        leaseRepo.Setup(l => l.FindActiveForWorkerAsync(WorkerId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((LeaseRecord?)null);
        runRepo.Setup(r => r.FindByStateAsync(RunState.Queued, It.IsAny<CancellationToken>()))
               .ReturnsAsync((IReadOnlyList<RunRecord>) new List<RunRecord> { run });
        artifactRepo.Setup(a => a.GetRecordAsync(
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(artifact);
        leaseRepo.Setup(l => l.HasActiveOfferForRunAsync(run.Id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false);

        var cmd = new PullJobCommand(WorkerId, RuntimeType, []);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<MatchResult.JobOffered>(
            because: "a job with no required capabilities is eligible for any worker of matching runtime");
    }

    // ── 7. Worker not registered → Error.NotFound ─────────────────────────────

    [Fact]
    public async Task HandleAsync_UnknownWorkerId_ReturnsNotFoundError()
    {
        // Arrange
        var handler = MakeHandler(
            out var workerRepo, out _, out _, out _,
            out _, out _);

        workerRepo.Setup(w => w.GetByIdAsync("unknown-worker", It.IsAny<CancellationToken>()))
                  .ReturnsAsync((WorkerRecord?)null);

        var cmd = new PullJobCommand("unknown-worker", RuntimeType, []);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue(
            because: "pulling a job for an unknown worker must result in an error, not a NoPendingJob");
        result.Error.Code.Should().Be("NotFound",
            because: "the orchestrator must reject unregistered workers explicitly");
    }

    // ── 8. Worker not AVAILABLE → NoPendingJob ─────────────────────────────────

    [Theory]
    [InlineData(WorkerState.Leased)]
    [InlineData(WorkerState.Running)]
    [InlineData(WorkerState.Offline)]
    public async Task HandleAsync_WorkerNotAvailable_ReturnsNoPendingJob(WorkerState nonAvailableState)
    {
        // Arrange
        var handler = MakeHandler(
            out var workerRepo, out _, out _, out var leaseRepo,
            out _, out _);

        var worker = MakeAvailableWorker();
        // Transition to the requested non-available state.
        if (nonAvailableState == WorkerState.Leased)  worker.MarkLeased();
        if (nonAvailableState == WorkerState.Running) { worker.MarkLeased(); worker.MarkRunning(); }
        if (nonAvailableState == WorkerState.Offline) worker.MarkOffline(FixedNow);

        workerRepo.Setup(w => w.GetByIdAsync(WorkerId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(worker);

        var cmd = new PullJobCommand(WorkerId, RuntimeType, []);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<MatchResult.NoPendingJob>(
            because: $"a worker in state {nonAvailableState} must not receive a new job offer");
    }

    // ── 9. Input validation ────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_BlankWorkerId_ReturnsValidationError(string blankWorkerId)
    {
        // Arrange
        var handler = MakeHandler(out _, out _, out _, out _, out _, out _);
        var cmd     = new PullJobCommand(blankWorkerId, RuntimeType, []);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Validation",
            because: "a blank WorkerId must be rejected before any I/O is attempted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_BlankRuntimeType_ReturnsValidationError(string blankRuntime)
    {
        // Arrange
        var handler = MakeHandler(out _, out _, out _, out _, out _, out _);
        var cmd     = new PullJobCommand(WorkerId, blankRuntime, []);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Validation",
            because: "a blank RuntimeType must be rejected before any I/O is attempted");
    }

    // ── 10. Empty queue → NoPendingJob ─────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_EmptyQueue_ReturnsNoPendingJob()
    {
        // Arrange
        var handler = MakeHandler(
            out var workerRepo, out var runRepo,
            out _, out var leaseRepo, out _, out _);

        workerRepo.Setup(w => w.GetByIdAsync(WorkerId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MakeAvailableWorker());
        leaseRepo.Setup(l => l.FindActiveForWorkerAsync(WorkerId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((LeaseRecord?)null);
        runRepo.Setup(r => r.FindByStateAsync(RunState.Queued, It.IsAny<CancellationToken>()))
               .ReturnsAsync((IReadOnlyList<RunRecord>) new List<RunRecord>()); // empty queue

        var cmd = new PullJobCommand(WorkerId, RuntimeType, []);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<MatchResult.NoPendingJob>(
            because: "an empty QUEUED run list must produce NoPendingJob without error");
    }

    // ── 11. Run already offered to another worker → skip ─────────────────────

    [Fact]
    public async Task HandleAsync_RunAlreadyOfferedToOtherWorker_ReturnsNoPendingJob()
    {
        // Arrange
        var handler = MakeHandler(
            out var workerRepo, out var runRepo,
            out var artifactRepo, out var leaseRepo,
            out _, out _);

        var run      = MakeQueuedRun();
        var artifact = MakeArtifact();

        workerRepo.Setup(w => w.GetByIdAsync(WorkerId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MakeAvailableWorker());
        leaseRepo.Setup(l => l.FindActiveForWorkerAsync(WorkerId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((LeaseRecord?)null);
        runRepo.Setup(r => r.FindByStateAsync(RunState.Queued, It.IsAny<CancellationToken>()))
               .ReturnsAsync((IReadOnlyList<RunRecord>) new List<RunRecord> { run });
        artifactRepo.Setup(a => a.GetRecordAsync(
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(artifact);

        // Another worker is already holding an offer for this run.
        leaseRepo.Setup(l => l.HasActiveOfferForRunAsync(run.Id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);

        var cmd = new PullJobCommand(WorkerId, RuntimeType, ["ClickAction"]);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<MatchResult.NoPendingJob>(
            because: "a run already offered to another worker must be skipped (invariant 3)");
    }

    // ── 12. MatchResult.JobOffered AttemptId is distinct from RunRecord.AttemptId

    [Fact]
    public async Task HandleAsync_JobOffered_AttemptIdIsFreshGuid_DistinctFromRunAttemptId()
    {
        // Arrange
        var handler = MakeHandler(
            out var workerRepo, out var runRepo,
            out var artifactRepo, out var leaseRepo,
            out _, out _);

        var run = MakeQueuedRun();
        SetupHappyPath(workerRepo, runRepo, artifactRepo, leaseRepo, run: run);

        var cmd = new PullJobCommand(WorkerId, RuntimeType, ["ClickAction"]);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var offered = (MatchResult.JobOffered) result.Value;

        offered.AttemptId.Should().NotBeEmpty(
            because: "the lease AttemptId fences subsequent AcceptLease calls");
        offered.AttemptId.Should().NotBe(run.AttemptId,
            because: "the lease creates its own fresh AttemptId; the run's AttemptId is separate");
    }

    // ── 13. Artifact soft-deleted → run skipped ───────────────────────────────

    [Fact]
    public async Task HandleAsync_ArtifactSoftDeleted_SkipsRunAndReturnsNoPendingJob()
    {
        // Arrange
        var handler = MakeHandler(
            out var workerRepo, out var runRepo,
            out var artifactRepo, out var leaseRepo,
            out _, out _);

        var run             = MakeQueuedRun();
        var deletedArtifact = MakeArtifact();
        deletedArtifact.SoftDelete();

        workerRepo.Setup(w => w.GetByIdAsync(WorkerId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MakeAvailableWorker());
        leaseRepo.Setup(l => l.FindActiveForWorkerAsync(WorkerId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((LeaseRecord?)null);
        runRepo.Setup(r => r.FindByStateAsync(RunState.Queued, It.IsAny<CancellationToken>()))
               .ReturnsAsync((IReadOnlyList<RunRecord>) new List<RunRecord> { run });
        artifactRepo.Setup(a => a.GetRecordAsync(
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(deletedArtifact);    // soft-deleted

        var cmd = new PullJobCommand(WorkerId, RuntimeType, []);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<MatchResult.NoPendingJob>(
            because: "a run whose artifact has been soft-deleted must be skipped during dispatch");
    }

    // ── 14. LeaseRecord domain invariants ─────────────────────────────────────

    [Fact]
    public void LeaseRecord_Accept_TransitionsToAcceptedAndSetsAcceptedAt()
    {
        // Arrange
        var now   = FixedNow;
        var lease = new LeaseRecord(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "worker-1", "art-1", "1.0", "hash-abc",
            offeredAt: now,
            expiresAt: now.AddSeconds(30));

        // Act
        lease.Accept(now.AddSeconds(10));

        // Assert
        lease.State.Should().Be(LeaseState.Accepted,
            because: "Accept must transition the lease from Offered to Accepted");
        lease.AcceptedAt.Should().Be(now.AddSeconds(10),
            because: "AcceptedAt must record the exact acceptance timestamp");
    }

    [Fact]
    public void LeaseRecord_Expire_TransitionsToExpired()
    {
        // Arrange
        var now   = FixedNow;
        var lease = new LeaseRecord(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "worker-1", "art-1", "1.0", "hash-abc",
            offeredAt: now,
            expiresAt: now.AddSeconds(30));

        // Act
        lease.Expire();

        // Assert
        lease.State.Should().Be(LeaseState.Expired,
            because: "Expire must transition an Offered lease to Expired");
    }

    [Fact]
    public void LeaseRecord_AcceptExpiredLease_Throws()
    {
        // Arrange
        var now   = FixedNow;
        var lease = new LeaseRecord(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "worker-1", "art-1", "1.0", "hash-abc",
            offeredAt: now,
            expiresAt: now.AddSeconds(30));

        lease.Expire();

        // Act
        var act = () => lease.Accept(now.AddSeconds(31));

        // Assert
        act.Should().Throw<InvalidOperationException>(
            because: "accepting an Expired lease must throw — the window has closed");
    }

    [Fact]
    public void LeaseRecord_ExpireAcceptedLease_ViaDedicatedMethod_SetsExpiredState()
    {
        // Arrange — OC-05 introduced ExpireAccepted() so the monitor can expire accepted leases.
        // Expire() (the original offer-window path) no longer guards against Accepted state;
        // ExpireAccepted() is the explicit, intentional path for the monitor service.
        var now   = FixedNow;
        var lease = new LeaseRecord(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "worker-1", "art-1", "1.0", "hash-abc",
            offeredAt: now,
            expiresAt: now.AddSeconds(30));

        lease.Accept(now.AddSeconds(5));

        // Act — use the dedicated OC-05 method for monitor-driven expiry of Accepted leases.
        lease.ExpireAccepted();

        // Assert
        lease.State.Should().Be(LeaseState.Accepted == lease.State ? LeaseState.Expired : lease.State,
            because: "ExpireAccepted must move the lease to Expired regardless of prior Accepted state");
        lease.State.Should().Be(LeaseState.Expired,
            because: "the monitor service expires Accepted leases via ExpireAccepted() (OC-05)");
    }

    [Fact]
    public void LeaseRecord_IsNonExpired_ReturnsTrueForOfferedAndAccepted()
    {
        // Arrange
        var now   = FixedNow;
        var offered = new LeaseRecord(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "w1", "a", "1", "h",
            offeredAt: now, expiresAt: now.AddSeconds(30));

        var accepted = new LeaseRecord(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "w2", "a", "1", "h",
            offeredAt: now, expiresAt: now.AddSeconds(30));
        accepted.Accept(now.AddSeconds(5));

        var expired = new LeaseRecord(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "w3", "a", "1", "h",
            offeredAt: now, expiresAt: now.AddSeconds(30));
        expired.Expire();

        // Act / Assert
        offered.IsNonExpired().Should().BeTrue(
            because: "an Offered lease is non-expired and blocks concurrent offers");
        accepted.IsNonExpired().Should().BeTrue(
            because: "an Accepted lease is non-expired and blocks concurrent offers");
        expired.IsNonExpired().Should().BeFalse(
            because: "an Expired lease must not block further offers for the same run");
    }
}
