using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Application.Recovery;
using Tracto.Orchestrator.Domain.Journal;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Domain.Workers;

// Explicit aliases to prevent CS0104 when both Application.Abstractions and
// Domain.Workers / Domain.Runs declare the same interface names.
using IWorkerRepository = Tracto.Orchestrator.Application.Abstractions.IWorkerRepository;
using IRunRepository    = Tracto.Orchestrator.Application.Abstractions.IRunRepository;
using IUnitOfWork       = Tracto.Orchestrator.Application.Abstractions.IUnitOfWork;
using IClock            = Tracto.Orchestrator.Application.Abstractions.IClock;

namespace Tracto.Orchestrator.Unit.Tests.Services;

/// <summary>
/// Unit tests for <see cref="RestartRecoveryService"/>.
///
/// OC-15 ACs verified:
///   - RUNNING run whose worker is offline → MarkUnknown; NeedsAttention=true; JournalEntry appended; CommitAsync called.
///   - No offline workers → no runs mutated; CommitAsync not called.
///   - Offline workers but no orphaned runs → CommitAsync not called.
///   - Empty DB → service completes without exception.
///   - Multiple runs → each gets a JournalEntry; CommitAsync called once.
///   - CommitAsync failure → RollbackAsync called; exception propagates.
/// </summary>
public sealed class RestartRecoveryServiceTests
{
    private static readonly DateTimeOffset T0 =
        new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static WorkerRecord MakeOfflineWorker(string workerId = "worker-1")
    {
        var w = new WorkerRecord(workerId, "Desktop", absenceThresholdSeconds: 120, registeredAt: T0);
        w.MarkOffline(T0.AddMinutes(1));
        return w;
    }

    private static RunRecord MakeRunningRun(string workflowId = "wf-001")
    {
        var r = new RunRecord(Guid.NewGuid(), workflowId, T0);
        r.Enqueue(T0);
        r.Lease(r.AttemptId, T0);
        r.Start(r.AttemptId, T0);
        return r;
    }

    /// <summary>
    /// Wraps mocked repositories in a minimal <see cref="IServiceScopeFactory"/> so the
    /// <see cref="RestartRecoveryService"/> (which now takes a scope factory) can resolve them.
    /// </summary>
    private static RestartRecoveryService BuildSut(
        IWorkerRepository workerRepo,
        IRunRepository runRepo,
        IJournalRepository journalRepo,
        IUnitOfWork uow,
        IClock? clock = null)
    {
        clock ??= Mock.Of<IClock>(c => c.UtcNow == T0);

        var spMock = new Mock<IServiceProvider>();
        spMock.Setup(p => p.GetService(typeof(IWorkerRepository))).Returns(workerRepo);
        spMock.Setup(p => p.GetService(typeof(IRunRepository))).Returns(runRepo);
        spMock.Setup(p => p.GetService(typeof(IJournalRepository))).Returns(journalRepo);
        spMock.Setup(p => p.GetService(typeof(IUnitOfWork))).Returns(uow);

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(spMock.Object);
        scopeMock.Setup(s => s.Dispose());

        var factoryMock = new Mock<IServiceScopeFactory>();
        factoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        return new RestartRecoveryService(
            factoryMock.Object,
            clock,
            NullLogger<RestartRecoveryService>.Instance);
    }

    // ─── AC1: RUNNING job + offline worker → NeedsAttention=true ─────────────

    [Fact]
    public async Task StartAsync_RunningJobWithOfflineWorker_MarksRunUnknown()
    {
        // Arrange
        var offlineWorker = MakeOfflineWorker("worker-1");
        var orphanedRun   = MakeRunningRun("wf-001");

        var workerRepoMock  = new Mock<IWorkerRepository>();
        var runRepoMock     = new Mock<IRunRepository>();
        var journalRepoMock = new Mock<IJournalRepository>();
        var uowMock         = new Mock<IUnitOfWork>();

        workerRepoMock
            .Setup(w => w.FindOfflineAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { offlineWorker });

        runRepoMock
            .Setup(r => r.FindByWorkerAndStateAsync("worker-1", RunState.Running, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { orphanedRun });

        runRepoMock.Setup(r => r.Update(It.IsAny<RunRecord>()));

        journalRepoMock
            .Setup(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        uowMock
            .Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = BuildSut(workerRepoMock.Object, runRepoMock.Object,
                           journalRepoMock.Object, uowMock.Object);

        // Act
        await sut.StartAsync(CancellationToken.None);

        // Assert — domain invariant 2
        orphanedRun.State.Should().Be(RunState.Unknown,
            "a RUNNING job with an offline worker must be moved to UNKNOWN");
        orphanedRun.NeedsAttention.Should().BeTrue(
            "domain invariant 2: UNKNOWN always requires NeedsAttention=true");
        orphanedRun.UnknownReason.Should().NotBeNullOrWhiteSpace(
            "domain invariant 2: UNKNOWN always requires a non-null UnknownReason");

        journalRepoMock.Verify(
            j => j.AppendAsync(
                It.Is<JournalEntry>(e => e.RunId == orphanedRun.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);

        uowMock.Verify(
            u => u.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─── AC2: No offline workers → nothing changes ────────────────────────────

    [Fact]
    public async Task StartAsync_NoOfflineWorkers_DoesNotMutateAnyRun()
    {
        // Arrange
        var workerRepoMock  = new Mock<IWorkerRepository>();
        var runRepoMock     = new Mock<IRunRepository>();
        var journalRepoMock = new Mock<IJournalRepository>();
        var uowMock         = new Mock<IUnitOfWork>();

        workerRepoMock
            .Setup(w => w.FindOfflineAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WorkerRecord>());

        var sut = BuildSut(workerRepoMock.Object, runRepoMock.Object,
                           journalRepoMock.Object, uowMock.Object);

        // Act
        await sut.StartAsync(CancellationToken.None);

        // Assert
        runRepoMock.Verify(r => r.Update(It.IsAny<RunRecord>()), Times.Never);
        journalRepoMock.Verify(
            j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()),
            Times.Never);
        uowMock.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── AC3: Offline workers but no orphaned RUNNING runs ────────────────────

    [Fact]
    public async Task StartAsync_OfflineWorkerButNoRunningRuns_DoesNotCommit()
    {
        // Arrange
        var offlineWorker = MakeOfflineWorker("worker-idle");

        var workerRepoMock  = new Mock<IWorkerRepository>();
        var runRepoMock     = new Mock<IRunRepository>();
        var journalRepoMock = new Mock<IJournalRepository>();
        var uowMock         = new Mock<IUnitOfWork>();

        workerRepoMock
            .Setup(w => w.FindOfflineAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { offlineWorker });

        runRepoMock
            .Setup(r => r.FindByWorkerAndStateAsync(
                It.IsAny<string>(), RunState.Running, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RunRecord>());

        var sut = BuildSut(workerRepoMock.Object, runRepoMock.Object,
                           journalRepoMock.Object, uowMock.Object);

        // Act
        await sut.StartAsync(CancellationToken.None);

        // Assert
        uowMock.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        journalRepoMock.Verify(
            j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─── AC4: Empty DB ────────────────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_EmptyDatabase_CompletesWithoutException()
    {
        // Arrange
        var workerRepoMock  = new Mock<IWorkerRepository>();
        var runRepoMock     = new Mock<IRunRepository>();
        var journalRepoMock = new Mock<IJournalRepository>();
        var uowMock         = new Mock<IUnitOfWork>();

        workerRepoMock
            .Setup(w => w.FindOfflineAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WorkerRecord>());

        var sut = BuildSut(workerRepoMock.Object, runRepoMock.Object,
                           journalRepoMock.Object, uowMock.Object);

        // Act & Assert
        var act = async () => await sut.StartAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    // ─── AC5: Multiple offline workers + multiple runs ────────────────────────

    [Fact]
    public async Task StartAsync_MultipleWorkersAndRuns_EachRunGetsJournalEntry()
    {
        // Arrange: two offline workers, each owning one orphaned run.
        var w1   = MakeOfflineWorker("worker-a");
        var w2   = MakeOfflineWorker("worker-b");
        var run1 = MakeRunningRun("wf-a");
        var run2 = MakeRunningRun("wf-b");

        var workerRepoMock  = new Mock<IWorkerRepository>();
        var runRepoMock     = new Mock<IRunRepository>();
        var journalRepoMock = new Mock<IJournalRepository>();
        var uowMock         = new Mock<IUnitOfWork>();

        workerRepoMock
            .Setup(w => w.FindOfflineAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { w1, w2 });

        runRepoMock
            .Setup(r => r.FindByWorkerAndStateAsync("worker-a", RunState.Running, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { run1 });
        runRepoMock
            .Setup(r => r.FindByWorkerAndStateAsync("worker-b", RunState.Running, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { run2 });

        runRepoMock.Setup(r => r.Update(It.IsAny<RunRecord>()));
        journalRepoMock
            .Setup(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        uowMock
            .Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = BuildSut(workerRepoMock.Object, runRepoMock.Object,
                           journalRepoMock.Object, uowMock.Object);

        // Act
        await sut.StartAsync(CancellationToken.None);

        // Assert: both runs marked UNKNOWN, 2 journal entries, 1 commit.
        run1.State.Should().Be(RunState.Unknown);
        run2.State.Should().Be(RunState.Unknown);
        run1.NeedsAttention.Should().BeTrue();
        run2.NeedsAttention.Should().BeTrue();

        journalRepoMock.Verify(
            j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2),
            "each orphaned run must produce exactly one JournalEntry");

        uowMock.Verify(
            u => u.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Once,
            "the whole batch is committed in a single CommitAsync call");
    }

    // ─── AC6: CommitAsync failure → RollbackAsync; exception propagates ───────

    [Fact]
    public async Task StartAsync_CommitAsyncThrows_RollbackCalledAndExceptionPropagates()
    {
        // Arrange
        var offlineWorker = MakeOfflineWorker("worker-fail");
        var orphanedRun   = MakeRunningRun("wf-fail");

        var workerRepoMock  = new Mock<IWorkerRepository>();
        var runRepoMock     = new Mock<IRunRepository>();
        var journalRepoMock = new Mock<IJournalRepository>();
        var uowMock         = new Mock<IUnitOfWork>();

        workerRepoMock
            .Setup(w => w.FindOfflineAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { offlineWorker });

        runRepoMock
            .Setup(r => r.FindByWorkerAndStateAsync(
                It.IsAny<string>(), RunState.Running, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { orphanedRun });

        runRepoMock.Setup(r => r.Update(It.IsAny<RunRecord>()));
        journalRepoMock
            .Setup(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        uowMock
            .Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated commit failure"));
        uowMock
            .Setup(u => u.RollbackAsync())
            .Returns(Task.CompletedTask);

        var sut = BuildSut(workerRepoMock.Object, runRepoMock.Object,
                           journalRepoMock.Object, uowMock.Object);

        // Act
        var act = async () => await sut.StartAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();

        uowMock.Verify(u => u.RollbackAsync(), Times.Once,
            "RollbackAsync must be called when CommitAsync throws");
    }

    // ─── AC7: StopAsync is always a no-op ────────────────────────────────────

    [Fact]
    public async Task StopAsync_AlwaysCompletesWithoutException()
    {
        var sut = BuildSut(
            Mock.Of<IWorkerRepository>(),
            Mock.Of<IRunRepository>(),
            Mock.Of<IJournalRepository>(),
            Mock.Of<IUnitOfWork>());

        var act = async () => await sut.StopAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }
}
