#nullable enable
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Application.Jobs;
using Tracto.Orchestrator.Domain.Artifacts;
using Tracto.Orchestrator.Domain.Journal;
using Tracto.Orchestrator.Domain.Leases;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Domain.Workers;
using Tracto.Orchestrator.Infrastructure.Clock;

namespace Tracto.Orchestrator.Unit.Tests.Runs;

/// <summary>
/// Unit tests for <see cref="PullJobCommandHandler"/> — story OC-03.
/// Each test covers exactly one observable behaviour.
/// FakeClock controls time; Moq stubs all external dependencies.
/// </summary>
public sealed class PullJobCommandTests
{
    // ── Fixed baseline ────────────────────────────────────────────────────────

    private static readonly DateTimeOffset FixedNow =
        new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    private const string WorkerId    = "worker-alpha";
    private const string RuntimeType = "dotnet";
    private const string ArtifactId  = "script-001";
    private const string Version     = "1.0.0";
    private const string ContentHash = "sha256-abc123";

    // SelectorContent with runtimeType = dotnet, no required capabilities, not headed.
    private const string DotnetSelector = "{\"runtimeType\":\"dotnet\",\"requiredCapabilities\":[],\"headed\":false}";

    // ── Builders ──────────────────────────────────────────────────────────────

    private static WorkerRecord AvailableWorker() =>
        new(WorkerId, RuntimeType, absenceThresholdSeconds: 60, registeredAt: FixedNow.AddHours(-1));

    private static ArtifactRecord MakeArtifact(string? selectorContent = DotnetSelector) =>
        new(ArtifactId, Version, "1", ContentHash,
            storedAt: FixedNow.AddDays(-1),
            signatureValid: true,
            selectorContent: selectorContent);

    private static RunRecord QueuedRun()
    {
        var r = new RunRecord(
            id:               Guid.NewGuid(),
            workflowId:       ArtifactId,
            createdAt:        FixedNow.AddMinutes(-5),
            correlationId:    Guid.NewGuid(),
            scriptArtifactId: ArtifactId,
            scriptVersion:    Version,
            schemaVersion:    "1",
            inputRefs:        ["input/a.json"],
            secretRefs:       ["secret/token"],
            deadline:         null,
            eventId:          null,
            triggerType:      TriggerType.Manual,
            businessJobId:    null);
        r.Enqueue(FixedNow.AddMinutes(-4));
        return r;
    }

    // ── Handler factory ───────────────────────────────────────────────────────

    /// <summary>
    /// Builds a fully-wired handler with Moq-backed dependencies.
    /// Default: one QUEUED run with dotnet selector, no active leases, worker is AVAILABLE.
    /// </summary>
    private static PullJobCommandHandler MakeHandler(
        out Mock<IRunRepository>      runRepo,
        out Mock<IWorkerRepository>   workerRepo,
        out Mock<IArtifactRepository> artifactRepo,
        out Mock<ILeaseRepository>    leaseRepo,
        out Mock<IJournalRepository>  journalRepo,
        out Mock<IUnitOfWork>         unitOfWork,
        WorkerRecord?   worker           = null,
        RunRecord?      explicitRun      = null,
        bool            workerFound      = true,
        bool            hasActiveLease   = false,
        bool            runAlreadyOffered = false,
        bool            sessionBlocked   = false,
        ArtifactRecord? artifact         = null,
        bool            noQueuedRuns     = false)
    {
        runRepo      = new Mock<IRunRepository>(MockBehavior.Strict);
        workerRepo   = new Mock<IWorkerRepository>(MockBehavior.Strict);
        artifactRepo = new Mock<IArtifactRepository>(MockBehavior.Strict);
        leaseRepo    = new Mock<ILeaseRepository>(MockBehavior.Strict);
        journalRepo  = new Mock<IJournalRepository>(MockBehavior.Strict);
        unitOfWork   = new Mock<IUnitOfWork>(MockBehavior.Strict);

        var clock = new FakeClock(FixedNow);

        // UnitOfWork lifecycle
        unitOfWork.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        // RollbackAsync is called by the try/finally in PullJobCommandHandler on every
        // non-committed exit path. Documented as safe on an empty or already-committed transaction.
        unitOfWork.Setup(u => u.RollbackAsync())
                  .Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.DisposeAsync())
                  .Returns(ValueTask.CompletedTask);

        // Worker lookup
        var resolvedWorker = workerFound ? (worker ?? AvailableWorker()) : null;
        workerRepo.Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(resolvedWorker);

        // Active lease check for this worker (Application.Abstractions method name: FindActiveForWorkerAsync)
        leaseRepo.Setup(r => r.FindActiveForWorkerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(hasActiveLease
                     ? new LeaseRecord(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                                       WorkerId, ArtifactId, Version, ContentHash,
                                       FixedNow.AddSeconds(-10), FixedNow.AddSeconds(20))
                     : (LeaseRecord?)null);

        // QUEUED runs
        IReadOnlyList<RunRecord> runs = noQueuedRuns
            ? []
            : [explicitRun ?? QueuedRun()];

        runRepo.Setup(r => r.FindByStateAsync(RunState.Queued, It.IsAny<CancellationToken>()))
               .ReturnsAsync(runs);

        // Artifact lookup
        var art = artifact ?? MakeArtifact();
        artifactRepo.Setup(r => r.GetRecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(art);

        // Per-run offer check (Application.Abstractions method name: HasActiveOfferForRunAsync → returns bool)
        leaseRepo.Setup(r => r.HasActiveOfferForRunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(runAlreadyOffered);

        // Session exclusivity check (Application.Abstractions method name: HasActiveSessionLeaseAsync)
        leaseRepo.Setup(r => r.HasActiveSessionLeaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(sessionBlocked);

        // Lease add
        leaseRepo.Setup(r => r.AddAsync(It.IsAny<LeaseRecord>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        // Journal append
        journalRepo.Setup(r => r.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
                   .Returns(Task.CompletedTask);

        return new PullJobCommandHandler(
            runRepo.Object,
            workerRepo.Object,
            artifactRepo.Object,
            leaseRepo.Object,
            journalRepo.Object,
            unitOfWork.Object,
            clock,
            NullLogger<PullJobCommandHandler>.Instance);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CapableWorker_ReturnsJobOffered()
    {
        // Arrange
        var handler = MakeHandler(out _, out _, out _, out _, out _, out _);
        var cmd = new PullJobCommand(WorkerId, RuntimeType, [], null);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<MatchResult.JobOffered>();
        var offered = (MatchResult.JobOffered)result.Value;
        offered.ArtifactHash.Should().Be(ContentHash);
        offered.InputRefs.Should().ContainSingle(r => r == "input/a.json");
        offered.SecretRefs.Should().ContainSingle(r => r == "secret/token");
    }

    [Fact]
    public async Task WrongRuntimeType_ReturnsNoPendingJob()
    {
        // Arrange — artifact requires "dotnet" but worker declares "python"
        var handler = MakeHandler(
            out _, out _, out _, out _, out _, out _,
            worker: new WorkerRecord("worker-py", "python", 60, FixedNow));

        var cmd = new PullJobCommand("worker-py", "python", [], null);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<MatchResult.NoPendingJob>();
    }

    [Fact]
    public async Task WorkerAlreadyHasActiveLease_ReturnsNoPendingJob()
    {
        // Arrange
        var handler = MakeHandler(
            out _, out _, out _, out _, out _, out _,
            hasActiveLease: true);

        var cmd = new PullJobCommand(WorkerId, RuntimeType, [], null);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<MatchResult.NoPendingJob>();
    }

    [Fact]
    public async Task RunAlreadyOfferedToAnotherWorker_ReturnsNoPendingJob()
    {
        // Arrange — the queued run has already been offered to a different worker
        var handler = MakeHandler(
            out _, out _, out _, out _, out _, out _,
            runAlreadyOffered: true);

        var cmd = new PullJobCommand(WorkerId, RuntimeType, [], null);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<MatchResult.NoPendingJob>();
    }

    [Fact]
    public async Task InteractiveSessionExclusivity_SecondHeadedWorkerBlocked()
    {
        // Arrange — another worker already holds a lease for this session
        const string headedSelector = "{\"runtimeType\":\"dotnet\",\"requiredCapabilities\":[],\"headed\":true}";
        var handler = MakeHandler(
            out _, out _, out _, out _, out _, out _,
            sessionBlocked: true,
            artifact: MakeArtifact(headedSelector));

        var cmd = new PullJobCommand(WorkerId, RuntimeType, [], "session-xyz");

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<MatchResult.NoPendingJob>();
    }

    [Fact]
    public async Task WrongCapabilityName_ReturnsNoPendingJob()
    {
        // Arrange — artifact requires "ClickAction" but worker offers nothing
        const string selector = "{\"runtimeType\":\"dotnet\",\"requiredCapabilities\":[\"ClickAction\"],\"headed\":false}";
        var handler = MakeHandler(
            out _, out _, out _, out _, out _, out _,
            artifact: MakeArtifact(selector));

        var cmd = new PullJobCommand(WorkerId, RuntimeType, [], null); // no capabilities declared

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<MatchResult.NoPendingJob>();
    }

    [Fact]
    public async Task CapableWorker_WithRequiredCapability_ReturnsJobOffered()
    {
        // Arrange — artifact requires "ClickAction"; worker satisfies it
        const string selector = "{\"runtimeType\":\"dotnet\",\"requiredCapabilities\":[\"ClickAction\"],\"headed\":false}";
        var handler = MakeHandler(
            out _, out _, out _, out _, out _, out _,
            artifact: MakeArtifact(selector));

        var cmd = new PullJobCommand(WorkerId, RuntimeType, ["ClickAction", "TypeAction"], null);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<MatchResult.JobOffered>();
    }

    [Fact]
    public async Task UnregisteredWorker_ReturnsNotFoundError()
    {
        // Arrange
        var handler = MakeHandler(
            out _, out _, out _, out _, out _, out _,
            workerFound: false);

        var cmd = new PullJobCommand("unknown-worker", RuntimeType, [], null);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound");
    }

    [Fact]
    public async Task JobOffered_LeaseHasCorrectExpiryAndFields()
    {
        // Arrange
        var handler = MakeHandler(
            out _, out _, out _, out var leaseRepo, out _, out _);

        LeaseRecord? captured = null;
        leaseRepo.Setup(r => r.AddAsync(It.IsAny<LeaseRecord>(), It.IsAny<CancellationToken>()))
                 .Callback<LeaseRecord, CancellationToken>((l, _) => captured = l)
                 .Returns(Task.CompletedTask);

        var cmd = new PullJobCommand(WorkerId, RuntimeType, [], null);

        // Act
        await handler.HandleAsync(cmd);

        // Assert
        captured.Should().NotBeNull();
        captured!.ExpiresAt.Should().Be(FixedNow.Add(PullJobCommandHandler.LeaseOfferWindow));
        captured.State.Should().Be(LeaseState.Offered);
        captured.WorkerId.Should().Be(WorkerId);
        captured.ArtifactHash.Should().Be(ContentHash);
        captured.InteractiveSessionId.Should().BeNull();
    }
}
