#nullable enable
using FluentAssertions;
using Moq;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Application.Workers;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Domain.Telemetry;
using Tracto.Orchestrator.Domain.Workers;

namespace Tracto.Orchestrator.Unit.Tests.Workers;

/// <summary>
/// Unit tests for <see cref="MarkWorkerOfflineCommandHandler"/> — story OC-01.
/// </summary>
public sealed class MarkWorkerOfflineCommandTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => FixedNow;
    }

    private static MarkWorkerOfflineCommandHandler MakeHandler(
        out Mock<IWorkerRepository> workerRepo,
        out Mock<IRunRepository>    runRepo,
        out Mock<IUnitOfWork>       unitOfWork)
    {
        workerRepo = new Mock<IWorkerRepository>(MockBehavior.Strict);
        runRepo    = new Mock<IRunRepository>(MockBehavior.Strict);
        unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);

        unitOfWork.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        var telemetry = new Mock<ITelemetryService>();
        telemetry.Setup(t => t.EmitAsync(It.IsAny<TelemetryEvent>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        return new MarkWorkerOfflineCommandHandler(
            workerRepo.Object,
            runRepo.Object,
            unitOfWork.Object,
            new FakeClock(),
            telemetry.Object);
    }

    // ── 1. Worker marked offline, State=OFFLINE, IsOnline=false ──────────────

    [Fact]
    public async Task HandleAsync_OnlineWorker_BecomesOffline()
    {
        // Arrange
        var handler = MakeHandler(out var workerRepo, out var runRepo, out _);

        var worker = new WorkerRecord("w-go-offline", "Desktop", 120, FixedNow.AddHours(-1));

        workerRepo.Setup(r => r.GetByIdAsync("w-go-offline", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(worker);
        workerRepo.Setup(r => r.Update(worker));

        runRepo.Setup(r => r.FindByWorkerAndStateAsync(
                "w-go-offline", RunState.Running, It.IsAny<CancellationToken>()))
               .ReturnsAsync(Array.Empty<RunRecord>());
        runRepo.Setup(r => r.FindByWorkerAndStateAsync(
                "w-go-offline", RunState.Leased, It.IsAny<CancellationToken>()))
               .ReturnsAsync(Array.Empty<RunRecord>());

        // Act
        var result = await handler.HandleAsync(new MarkWorkerOfflineCommand("w-go-offline"));

        // Assert
        result.IsSuccess.Should().BeTrue();
        worker.IsOnline.Should().BeFalse();
        worker.State.Should().Be(WorkerState.Offline);
    }

    // ── 2. RUNNING runs for the worker become UNKNOWN + NeedsAttention ────────

    [Fact]
    public async Task HandleAsync_WorkerWithRunningRuns_RunsMarkedUnknown()
    {
        // Arrange
        var handler = MakeHandler(out var workerRepo, out var runRepo, out _);

        var worker = new WorkerRecord("w-with-runs", "Desktop", 120, FixedNow.AddHours(-1));

        workerRepo.Setup(r => r.GetByIdAsync("w-with-runs", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(worker);
        workerRepo.Setup(r => r.Update(worker));

        // Build a run that is currently RUNNING.
        var run = new RunRecord(Guid.NewGuid(), "wf-1", FixedNow.AddMinutes(-30));
        run.Enqueue(FixedNow.AddMinutes(-29));
        run.Lease(run.AttemptId, "w-with-runs", FixedNow.AddMinutes(-28));
        run.Start(run.AttemptId, FixedNow.AddMinutes(-27));

        runRepo.Setup(r => r.FindByWorkerAndStateAsync(
                "w-with-runs", RunState.Running, It.IsAny<CancellationToken>()))
               .ReturnsAsync([run]);
        runRepo.Setup(r => r.FindByWorkerAndStateAsync(
                "w-with-runs", RunState.Leased, It.IsAny<CancellationToken>()))
               .ReturnsAsync(Array.Empty<RunRecord>());

        runRepo.Setup(r => r.Update(run));

        // Act
        var result = await handler.HandleAsync(new MarkWorkerOfflineCommand("w-with-runs"));

        // Assert
        result.IsSuccess.Should().BeTrue();
        run.State.Should().Be(RunState.Unknown);
        run.NeedsAttention.Should().BeTrue();
        run.UnknownReason.Should().NotBeNullOrWhiteSpace();

        runRepo.Verify(r => r.Update(run), Times.Once);
    }

    // ── 2b. LEASED runs for the worker also become UNKNOWN + NeedsAttention ──────

    [Fact]
    public async Task HandleAsync_WorkerWithLeasedRun_LeasedRunMarkedUnknown()
    {
        // Arrange
        var handler = MakeHandler(out var workerRepo, out var runRepo, out _);

        var worker = new WorkerRecord("w-leased-runs", "Desktop", 120, FixedNow.AddHours(-1));

        workerRepo.Setup(r => r.GetByIdAsync("w-leased-runs", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(worker);
        workerRepo.Setup(r => r.Update(worker));

        // Build a run that is LEASED (worker claimed it but never called Start).
        var leasedRun = new RunRecord(Guid.NewGuid(), "wf-leased", FixedNow.AddMinutes(-30));
        leasedRun.Enqueue(FixedNow.AddMinutes(-29));
        leasedRun.Lease(leasedRun.AttemptId, "w-leased-runs", FixedNow.AddMinutes(-28));
        // NOTE: leasedRun.Start() is intentionally NOT called — the run stays LEASED.

        // Running-state query returns empty.
        runRepo.Setup(r => r.FindByWorkerAndStateAsync(
                "w-leased-runs", RunState.Running, It.IsAny<CancellationToken>()))
               .ReturnsAsync(Array.Empty<RunRecord>());

        // Leased-state query returns the leased run.
        runRepo.Setup(r => r.FindByWorkerAndStateAsync(
                "w-leased-runs", RunState.Leased, It.IsAny<CancellationToken>()))
               .ReturnsAsync([leasedRun]);

        runRepo.Setup(r => r.Update(leasedRun));

        // Act
        var result = await handler.HandleAsync(new MarkWorkerOfflineCommand("w-leased-runs"));

        // Assert
        result.IsSuccess.Should().BeTrue();
        leasedRun.State.Should().Be(RunState.Unknown,
            "a leased but not-started run is orphaned when its worker goes offline");
        leasedRun.NeedsAttention.Should().BeTrue();
        leasedRun.UnknownReason.Should().NotBeNullOrWhiteSpace();

        runRepo.Verify(r => r.Update(leasedRun), Times.Once);
    }

    // ── 3. Worker not found → NotFound error ─────────────────────────────────

    [Fact]
    public async Task HandleAsync_UnknownWorkerId_ReturnsNotFoundError()
    {
        // Arrange
        var handler = MakeHandler(out var workerRepo, out _, out _);

        workerRepo.Setup(r => r.GetByIdAsync("w-missing", It.IsAny<CancellationToken>()))
                  .ReturnsAsync((WorkerRecord?)null);

        // Act
        var result = await handler.HandleAsync(new MarkWorkerOfflineCommand("w-missing"));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound");
        result.Error.Message.Should().Contain("w-missing");
    }

    // ── 4. Worker with no RUNNING runs → still marked offline cleanly ─────────

    [Fact]
    public async Task HandleAsync_WorkerWithNoRunningRuns_SucceedsWithNoRunUpdates()
    {
        // Arrange
        var handler = MakeHandler(out var workerRepo, out var runRepo, out _);

        var worker = new WorkerRecord("w-idle", "Desktop", 120, FixedNow.AddHours(-2));

        workerRepo.Setup(r => r.GetByIdAsync("w-idle", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(worker);
        workerRepo.Setup(r => r.Update(worker));

        runRepo.Setup(r => r.FindByWorkerAndStateAsync(
                "w-idle", RunState.Running, It.IsAny<CancellationToken>()))
               .ReturnsAsync(Array.Empty<RunRecord>());
        runRepo.Setup(r => r.FindByWorkerAndStateAsync(
                "w-idle", RunState.Leased, It.IsAny<CancellationToken>()))
               .ReturnsAsync(Array.Empty<RunRecord>());

        // Act
        var result = await handler.HandleAsync(new MarkWorkerOfflineCommand("w-idle"));

        // Assert
        result.IsSuccess.Should().BeTrue();
        worker.State.Should().Be(WorkerState.Offline);

        runRepo.Verify(r => r.Update(It.IsAny<RunRecord>()), Times.Never);
    }
}
