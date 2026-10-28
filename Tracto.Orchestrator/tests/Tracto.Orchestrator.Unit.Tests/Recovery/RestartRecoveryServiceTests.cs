using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Application.Recovery;
using Tracto.Orchestrator.Domain.Journal;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Domain.Workers;
using Tracto.Orchestrator.Infrastructure.Clock;

namespace Tracto.Orchestrator.Unit.Tests.Recovery;

/// <summary>
/// Unit tests for <see cref="RestartRecoveryService"/>.
///
/// OC-15 ACs verified:
///   - RUNNING job with offline worker → NeedsAttention set and UnknownReason populated.
///   - No offline workers → nothing changed.
///   - Offline worker with no RUNNING runs → nothing committed.
///   - Journal entry is appended for every recovered run.
///   - CommitAsync called exactly once per batch.
///   - RollbackAsync called when CommitAsync throws, exception re-thrown.
///   - Schema not ready (no such table) → recovery skipped gracefully, no throw.
/// </summary>
public sealed class RestartRecoveryServiceTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2024, 6, 15, 8, 0, 0, TimeSpan.Zero);

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="RestartRecoveryService"/> backed by mock repositories.
    /// The new constructor takes <see cref="IServiceScopeFactory"/> (because repositories
    /// are Scoped and cannot be injected directly into the root-container IHostedService).
    /// This helper wraps the mocks in a minimal IServiceScopeFactory stand-in.
    /// </summary>
    /// <param name="clock">Pass a FakeClock or leave null to get one seeded at FixedNow.</param>
    private static RestartRecoveryService Build(
        IWorkerRepository  workers,
        IRunRepository     runs,
        IJournalRepository journal,
        IUnitOfWork        uow,
        Tracto.Orchestrator.Application.Abstractions.IClock? clock = null)
    {
        Tracto.Orchestrator.Application.Abstractions.IClock resolved =
            clock ?? new FakeClock(FixedNow);

        // Build a minimal DI scope that returns the mock services.
        var spMock = new Mock<IServiceProvider>();
        spMock.Setup(p => p.GetService(typeof(IWorkerRepository))).Returns(workers);
        spMock.Setup(p => p.GetService(typeof(IRunRepository))).Returns(runs);
        spMock.Setup(p => p.GetService(typeof(IJournalRepository))).Returns(journal);
        spMock.Setup(p => p.GetService(typeof(IUnitOfWork))).Returns(uow);

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(spMock.Object);
        scopeMock.Setup(s => s.Dispose());

        var factoryMock = new Mock<IServiceScopeFactory>();
        factoryMock
            .Setup(f => f.CreateScope())
            .Returns(scopeMock.Object);

        return new RestartRecoveryService(
            factoryMock.Object,
            resolved,
            NullLogger<RestartRecoveryService>.Instance);
    }

    private static WorkerRecord OfflineWorker(string id = "worker-dead")
    {
        var w = new WorkerRecord(id, "Desktop", 60, FixedNow.AddMinutes(-10));
        w.MarkOffline(FixedNow.AddMinutes(-5));
        return w;
    }

    private static RunRecord RunningRun(string workerId)
    {
        var now = FixedNow.AddMinutes(-20);
        var run = new RunRecord(Guid.NewGuid(), "wf-test", now);
        run.Enqueue(now);
        run.Lease(run.AttemptId, workerId, now);
        run.Start(run.AttemptId, now);
        return run;
    }

    // ─── AC: RUNNING job with offline worker → NeedsAttention set ─────────────

    [Fact]
    public async Task StartAsync_RunningJobWithOfflineWorker_SetsNeedsAttention()
    {
        // Arrange
        var offlineWorker = OfflineWorker("worker-dead-1");
        var orphanedRun   = RunningRun(offlineWorker.WorkerId);

        var mockWorkers = new Mock<IWorkerRepository>();
        mockWorkers.Setup(r => r.FindOfflineAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<WorkerRecord> { offlineWorker });

        RunRecord? capturedRun = null;
        var mockRuns = new Mock<IRunRepository>();
        mockRuns.Setup(r => r.FindByWorkerAndStateAsync(
                    offlineWorker.WorkerId, RunState.Running, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RunRecord> { orphanedRun });
        mockRuns.Setup(r => r.Update(It.IsAny<RunRecord>()))
                .Callback<RunRecord>(r => capturedRun = r);

        var mockJournal = new Mock<IJournalRepository>();
        var mockUow     = new Mock<IUnitOfWork>();

        var sut = Build(mockWorkers.Object, mockRuns.Object, mockJournal.Object, mockUow.Object);

        // Act
        await sut.StartAsync(CancellationToken.None);

        // Assert
        capturedRun.Should().NotBeNull(
            because: "IRunRepository.Update must be called for the orphaned run");
        capturedRun!.NeedsAttention.Should().BeTrue(
            because: "RUNNING run with offline worker must have NeedsAttention=true (invariant 2)");
        capturedRun.UnknownReason.Should().Be(
            "Worker absent at restart",
            because: "the canonical UnknownReason must be applied");
        capturedRun.State.Should().Be(RunState.Unknown,
            because: "the run must transition to UNKNOWN state");
    }

    [Fact]
    public async Task StartAsync_RunningJobWithOfflineWorker_CommitsExactlyOnce()
    {
        // Arrange
        var offlineWorker = OfflineWorker("worker-dead-2");
        var orphanedRun   = RunningRun(offlineWorker.WorkerId);

        var mockWorkers = new Mock<IWorkerRepository>();
        mockWorkers.Setup(r => r.FindOfflineAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<WorkerRecord> { offlineWorker });

        var mockRuns = new Mock<IRunRepository>();
        mockRuns.Setup(r => r.FindByWorkerAndStateAsync(
                    offlineWorker.WorkerId, RunState.Running, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RunRecord> { orphanedRun });

        var mockJournal = new Mock<IJournalRepository>();
        var mockUow     = new Mock<IUnitOfWork>();
        mockUow.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var sut = Build(mockWorkers.Object, mockRuns.Object, mockJournal.Object, mockUow.Object);

        // Act
        await sut.StartAsync(CancellationToken.None);

        // Assert — Moq Verify; third arg is a plain failure message, not a named 'because'
        mockUow.Verify(
            u => u.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Once,
            "exactly one CommitAsync must be called for a single recovery batch");
    }

    [Fact]
    public async Task StartAsync_RunningJobWithOfflineWorker_AppendsJournalEntry()
    {
        // Arrange
        var offlineWorker = OfflineWorker("worker-dead-3");
        var orphanedRun   = RunningRun(offlineWorker.WorkerId);

        var mockWorkers = new Mock<IWorkerRepository>();
        mockWorkers.Setup(r => r.FindOfflineAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<WorkerRecord> { offlineWorker });

        var mockRuns = new Mock<IRunRepository>();
        mockRuns.Setup(r => r.FindByWorkerAndStateAsync(
                    offlineWorker.WorkerId, RunState.Running, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RunRecord> { orphanedRun });

        JournalEntry? capturedEntry = null;
        var mockJournal = new Mock<IJournalRepository>();
        mockJournal.Setup(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
                   .Callback<JournalEntry, CancellationToken>((e, _) => capturedEntry = e);

        var mockUow = new Mock<IUnitOfWork>();
        mockUow.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var sut = Build(mockWorkers.Object, mockRuns.Object, mockJournal.Object, mockUow.Object);

        // Act
        await sut.StartAsync(CancellationToken.None);

        // Assert
        capturedEntry.Should().NotBeNull(
            because: "a JournalEntry must be appended for each recovered run");
        capturedEntry!.RunId.Should().Be(orphanedRun.Id,
            because: "journal entry must reference the recovered run");
        capturedEntry.EventType.Should().Be("WorkerAbsentAtRestart",
            because: "the canonical event type must be used");
        capturedEntry.OccurredAt.Should().Be(FixedNow,
            because: "journal timestamp must come from the FakeClock");
    }

    // ─── AC: No offline workers → nothing touched ─────────────────────────────

    [Fact]
    public async Task StartAsync_NoOfflineWorkers_NeverCallsCommit()
    {
        // Arrange
        var mockWorkers = new Mock<IWorkerRepository>();
        mockWorkers.Setup(r => r.FindOfflineAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<WorkerRecord>());

        var mockRuns    = new Mock<IRunRepository>();
        var mockJournal = new Mock<IJournalRepository>();
        var mockUow     = new Mock<IUnitOfWork>();

        var sut = Build(mockWorkers.Object, mockRuns.Object, mockJournal.Object, mockUow.Object);

        // Act
        await sut.StartAsync(CancellationToken.None);

        // Assert
        mockUow.Verify(
            u => u.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Never,
            "when no offline workers exist there is nothing to commit");
        mockRuns.Verify(
            r => r.Update(It.IsAny<RunRecord>()),
            Times.Never,
            "no runs should be touched when all workers are online");
    }

    // ─── AC: Offline worker with no RUNNING runs → nothing committed ───────────

    [Fact]
    public async Task StartAsync_OfflineWorkerButNoRunningRuns_NeverCallsCommit()
    {
        // Arrange
        var offlineWorker = OfflineWorker("worker-dead-no-runs");

        var mockWorkers = new Mock<IWorkerRepository>();
        mockWorkers.Setup(r => r.FindOfflineAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<WorkerRecord> { offlineWorker });

        var mockRuns = new Mock<IRunRepository>();
        mockRuns.Setup(r => r.FindByWorkerAndStateAsync(
                    offlineWorker.WorkerId, RunState.Running, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RunRecord>());

        var mockJournal = new Mock<IJournalRepository>();
        var mockUow     = new Mock<IUnitOfWork>();

        var sut = Build(mockWorkers.Object, mockRuns.Object, mockJournal.Object, mockUow.Object);

        // Act
        await sut.StartAsync(CancellationToken.None);

        // Assert
        mockUow.Verify(
            u => u.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Never,
            "an offline worker with no RUNNING runs should not trigger a commit");
        mockJournal.Verify(
            j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "no journal entries should be written when there are no orphaned runs");
    }

    // ─── AC: Multiple offline workers, multiple orphaned runs ─────────────────

    [Fact]
    public async Task StartAsync_MultipleOfflineWorkersWithRuns_RecoverAll()
    {
        // Arrange
        var worker1 = OfflineWorker("worker-multi-1");
        var worker2 = OfflineWorker("worker-multi-2");
        var run1    = RunningRun(worker1.WorkerId);
        var run2a   = RunningRun(worker2.WorkerId);
        var run2b   = RunningRun(worker2.WorkerId);

        var mockWorkers = new Mock<IWorkerRepository>();
        mockWorkers.Setup(r => r.FindOfflineAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<WorkerRecord> { worker1, worker2 });

        var mockRuns = new Mock<IRunRepository>();
        mockRuns.Setup(r => r.FindByWorkerAndStateAsync(
                    worker1.WorkerId, RunState.Running, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RunRecord> { run1 });
        mockRuns.Setup(r => r.FindByWorkerAndStateAsync(
                    worker2.WorkerId, RunState.Running, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RunRecord> { run2a, run2b });

        var mockJournal = new Mock<IJournalRepository>();
        var mockUow     = new Mock<IUnitOfWork>();
        mockUow.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var sut = Build(mockWorkers.Object, mockRuns.Object, mockJournal.Object, mockUow.Object);

        // Act
        await sut.StartAsync(CancellationToken.None);

        // Assert — Update called three times (once per orphaned run)
        mockRuns.Verify(
            r => r.Update(It.IsAny<RunRecord>()),
            Times.Exactly(3),
            "each of the 3 orphaned runs must be updated");

        // One journal entry per recovered run
        mockJournal.Verify(
            j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3),
            "one journal entry per recovered run");

        // Single commit for the whole batch
        mockUow.Verify(
            u => u.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Once,
            "the entire batch must be committed in one CommitAsync call");

        // Domain state on all three runs
        run1.State.Should().Be(RunState.Unknown);
        run2a.State.Should().Be(RunState.Unknown);
        run2b.State.Should().Be(RunState.Unknown);
    }

    // ─── AC: CommitAsync throws → RollbackAsync called, exception propagated ──

    [Fact]
    public async Task StartAsync_CommitThrows_CallsRollbackAndRethrows()
    {
        // Arrange
        var offlineWorker = OfflineWorker("worker-commit-fail");
        var orphanedRun   = RunningRun(offlineWorker.WorkerId);

        var mockWorkers = new Mock<IWorkerRepository>();
        mockWorkers.Setup(r => r.FindOfflineAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<WorkerRecord> { offlineWorker });

        var mockRuns = new Mock<IRunRepository>();
        mockRuns.Setup(r => r.FindByWorkerAndStateAsync(
                    offlineWorker.WorkerId, RunState.Running, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RunRecord> { orphanedRun });

        var mockJournal = new Mock<IJournalRepository>();

        var mockUow = new Mock<IUnitOfWork>();
        mockUow.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("DB failure"));
        mockUow.Setup(u => u.RollbackAsync()).Returns(Task.CompletedTask);
        mockUow.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var sut = Build(mockWorkers.Object, mockRuns.Object, mockJournal.Object, mockUow.Object);

        // Act
        var act = async () => await sut.StartAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>(
            because: "a commit failure must propagate so the host can abort startup");

        mockUow.Verify(
            u => u.RollbackAsync(),
            Times.Once,
            "rollback must be attempted when commit throws");
    }

    // ─── AC: Schema not ready → recovery skipped gracefully ──────────────────

    [Fact]
    public async Task StartAsync_SchemaNotReady_SkipsRecoveryWithoutThrowing()
    {
        // Arrange — simulate "no such table" from SQLite before migrations run.
        var mockWorkers = new Mock<IWorkerRepository>();
        mockWorkers.Setup(r => r.FindOfflineAsync(It.IsAny<CancellationToken>()))
                   .ThrowsAsync(new InvalidOperationException(
                       "SQLite Error 1: 'no such table: WorkerRecords'."));

        var mockRuns    = new Mock<IRunRepository>();
        var mockJournal = new Mock<IJournalRepository>();
        var mockUow     = new Mock<IUnitOfWork>();

        var sut = Build(mockWorkers.Object, mockRuns.Object, mockJournal.Object, mockUow.Object);

        // Act
        var act = async () => await sut.StartAsync(CancellationToken.None);

        // Assert — must not throw
        await act.Should().NotThrowAsync(
            because: "a 'no such table' exception at startup means migrations have not run yet " +
                     "and recovery should be skipped rather than crashing the host");

        mockUow.Verify(
            u => u.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Never,
            "nothing should be committed when recovery is skipped");
    }

    [Fact]
    public async Task StartAsync_SchemaNotReadyViaInnerException_SkipsRecoveryWithoutThrowing()
    {
        // Arrange — "no such table" buried inside a wrapper exception.
        var inner = new Exception("no such table: WorkerRecords");
        var outer = new InvalidOperationException("Database query failed.", inner);

        var mockWorkers = new Mock<IWorkerRepository>();
        mockWorkers.Setup(r => r.FindOfflineAsync(It.IsAny<CancellationToken>()))
                   .ThrowsAsync(outer);

        var mockRuns    = new Mock<IRunRepository>();
        var mockJournal = new Mock<IJournalRepository>();
        var mockUow     = new Mock<IUnitOfWork>();

        var sut = Build(mockWorkers.Object, mockRuns.Object, mockJournal.Object, mockUow.Object);

        // Act & Assert
        var act = async () => await sut.StartAsync(CancellationToken.None);
        await act.Should().NotThrowAsync(
            because: "schema-not-ready detection must walk the full InnerException chain");
    }

    // ─── AC: StopAsync is always a no-op ─────────────────────────────────────

    [Fact]
    public async Task StopAsync_Always_ReturnsCompletedTask()
    {
        var mockWorkers = new Mock<IWorkerRepository>();
        var mockRuns    = new Mock<IRunRepository>();
        var mockJournal = new Mock<IJournalRepository>();
        var mockUow     = new Mock<IUnitOfWork>();

        var sut = Build(mockWorkers.Object, mockRuns.Object, mockJournal.Object, mockUow.Object);

        var act = async () => await sut.StopAsync(CancellationToken.None);
        await act.Should().NotThrowAsync(
            because: "StopAsync is a no-op and must complete successfully");
    }

    // ─── AC: Journal entry OccurredAt matches FakeClock.UtcNow ───────────────

    [Fact]
    public async Task StartAsync_JournalEntryTimestamp_MatchesFakeClockUtcNow()
    {
        // Arrange
        var seed          = new DateTimeOffset(2025, 3, 10, 14, 30, 0, TimeSpan.Zero);
        var fakeClock     = new FakeClock(seed);
        var offlineWorker = OfflineWorker("worker-clock-check");
        var orphanedRun   = RunningRun(offlineWorker.WorkerId);

        var mockWorkers = new Mock<IWorkerRepository>();
        mockWorkers.Setup(r => r.FindOfflineAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<WorkerRecord> { offlineWorker });

        var mockRuns = new Mock<IRunRepository>();
        mockRuns.Setup(r => r.FindByWorkerAndStateAsync(
                    offlineWorker.WorkerId, RunState.Running, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RunRecord> { orphanedRun });

        JournalEntry? capturedEntry = null;
        var mockJournal = new Mock<IJournalRepository>();
        mockJournal.Setup(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
                   .Callback<JournalEntry, CancellationToken>((e, _) => capturedEntry = e);

        var mockUow = new Mock<IUnitOfWork>();
        mockUow.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var sut = Build(mockWorkers.Object, mockRuns.Object, mockJournal.Object, mockUow.Object, fakeClock);

        // Act
        await sut.StartAsync(CancellationToken.None);

        // Assert
        capturedEntry.Should().NotBeNull();
        capturedEntry!.OccurredAt.Should().Be(seed,
            because: "the journal entry timestamp must be the instant reported by the IClock");
    }
}
