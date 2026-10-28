#nullable enable
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Application.Workers;
using Tracto.Orchestrator.Domain.Telemetry;
using Tracto.Orchestrator.Domain.Workers;

namespace Tracto.Orchestrator.Unit.Tests.Workers;

/// <summary>
/// Unit tests for <see cref="RegisterWorkerCommandHandler"/> — story OC-01.
/// Each test covers exactly one observable behaviour; FakeClock replaces the wall clock.
/// </summary>
public sealed class RegisterWorkerCommandTests
{
    // ── Test infrastructure ────────────────────────────────────────────────────

    private static readonly DateTimeOffset FixedNow =
        new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Minimal clock stub that always returns <see cref="FixedNow"/>.</summary>
    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => FixedNow;
    }

    /// <summary>
    /// Creates a fully wired handler with fresh Moq-backed dependencies.
    /// Out-params expose the mocks for assertion.
    /// </summary>
    private static RegisterWorkerCommandHandler MakeHandler(
        out Mock<IWorkerRepository> workerRepo,
        out Mock<IUnitOfWork>       unitOfWork)
    {
        workerRepo = new Mock<IWorkerRepository>(MockBehavior.Strict);
        unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);

        // Default: BeginAsync and CommitAsync succeed silently.
        unitOfWork.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        var telemetry = new Mock<ITelemetryService>();
        telemetry.Setup(t => t.EmitAsync(It.IsAny<TelemetryEvent>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        return new RegisterWorkerCommandHandler(
            workerRepo.Object,
            unitOfWork.Object,
            new FakeClock(),
            telemetry.Object,
            NullLogger<RegisterWorkerCommandHandler>.Instance);
    }

    // ── 1. Valid first-time registration ──────────────────────────────────────

    [Fact]
    public async Task HandleAsync_NewWorker_ReturnsSuccessWithWorkerId()
    {
        // Arrange
        var handler = MakeHandler(out var repoMock, out _);

        repoMock.Setup(r => r.GetByIdAsync("w-new", It.IsAny<CancellationToken>()))
                .ReturnsAsync((WorkerRecord?)null);

        WorkerRecord? captured = null;
        repoMock.Setup(r => r.AddAsync(It.IsAny<WorkerRecord>(), It.IsAny<CancellationToken>()))
                .Callback<WorkerRecord, CancellationToken>((w, _) => captured = w)
                .Returns(Task.CompletedTask);

        var cmd = new RegisterWorkerCommand(
            WorkerId:               "w-new",
            RuntimeType:            "Desktop",
            ActionNames:            ["ClickAction", "TypeAction"],
            CapabilityDigest:       null,
            AbsenceThresholdSeconds: 120);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.WorkerId.Should().Be("w-new");

        captured.Should().NotBeNull();
        captured!.WorkerId.Should().Be("w-new");
        captured.RuntimeType.Should().Be("Desktop");
        captured.State.Should().Be(WorkerState.Available);
        captured.IsOnline.Should().BeTrue();
        captured.AbsenceThresholdSeconds.Should().Be(120);
        captured.Capabilities.Should().NotBeNull();
        captured.Capabilities!.ActionNames.Should().BeEquivalentTo(["ClickAction", "TypeAction"]);
    }

    // ── 2a. Missing WorkerId → validation error ───────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_BlankWorkerId_ReturnsValidationError(string workerId)
    {
        // Arrange
        var handler = MakeHandler(out _, out _);

        var cmd = new RegisterWorkerCommand(
            WorkerId:               workerId,
            RuntimeType:            "Desktop",
            ActionNames:            [],
            CapabilityDigest:       null,
            AbsenceThresholdSeconds: 60);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Validation");
        result.Error.Message.Should().Contain("workerId");
    }

    // ── 2b. Missing RuntimeType → validation error ────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_BlankRuntimeType_ReturnsValidationError(string runtimeType)
    {
        // Arrange
        var handler = MakeHandler(out _, out _);

        var cmd = new RegisterWorkerCommand(
            WorkerId:               "w-1",
            RuntimeType:            runtimeType,
            ActionNames:            [],
            CapabilityDigest:       null,
            AbsenceThresholdSeconds: 60);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Validation");
        result.Error.Message.Should().Contain("runtimeType");
    }

    // ── 3. Re-registration updates LastRegisteredAt, does NOT release leases ──

    [Fact]
    public async Task HandleAsync_ReRegistration_UpdatesLastRegisteredAtAndKeepsState()
    {
        // Arrange
        var handler = MakeHandler(out var repoMock, out _);

        // Existing worker is currently leased — must NOT be released.
        var existingWorker = new WorkerRecord("w-existing", "Desktop", 60, FixedNow.AddMinutes(-5));
        existingWorker.MarkLeased();

        repoMock.Setup(r => r.GetByIdAsync("w-existing", It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingWorker);

        repoMock.Setup(r => r.Update(It.IsAny<WorkerRecord>()));

        var cmd = new RegisterWorkerCommand(
            WorkerId:               "w-existing",
            RuntimeType:            "Desktop",
            ActionNames:            ["NewAction"],
            CapabilityDigest:       null,
            AbsenceThresholdSeconds: 180);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert — success
        result.IsSuccess.Should().BeTrue();
        result.Value.WorkerId.Should().Be("w-existing");

        // State must stay LEASED (lease not released by re-registration).
        existingWorker.State.Should().Be(WorkerState.Leased);

        // Mutable fields updated.
        existingWorker.AbsenceThresholdSeconds.Should().Be(180);
        existingWorker.LastRegisteredAt.Should().Be(FixedNow);
        existingWorker.Capabilities.Should().NotBeNull();
        existingWorker.Capabilities!.ActionNames.Should().BeEquivalentTo(["NewAction"]);

        // AddAsync must NOT be called on re-registration.
        repoMock.Verify(r => r.AddAsync(It.IsAny<WorkerRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── 3b. Re-registration of an OFFLINE worker brings it back online ─────────

    [Fact]
    public async Task HandleAsync_ReRegistrationWhileOffline_WorkerBecomesAvailableAndOnline()
    {
        // Arrange
        var handler = MakeHandler(out var repoMock, out _);

        // Worker was previously marked offline.
        var offlineWorker = new WorkerRecord("w-offline", "Desktop", 60, FixedNow.AddHours(-2));
        offlineWorker.MarkOffline(FixedNow.AddHours(-1));

        repoMock.Setup(r => r.GetByIdAsync("w-offline", It.IsAny<CancellationToken>()))
                .ReturnsAsync(offlineWorker);
        repoMock.Setup(r => r.Update(It.IsAny<WorkerRecord>()));

        var cmd = new RegisterWorkerCommand(
            WorkerId:               "w-offline",
            RuntimeType:            "Desktop",
            ActionNames:            ["ClickAction"],
            CapabilityDigest:       null,
            AbsenceThresholdSeconds: 120);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert — re-registration must bring the worker back online.
        result.IsSuccess.Should().BeTrue();
        offlineWorker.IsOnline.Should().BeTrue("re-registration signals the worker is alive");
        offlineWorker.State.Should().Be(WorkerState.Available,
            "a previously offline worker should return to AVAILABLE on re-registration");
    }

    // ── 4. CapabilityDigest mismatch → registered with warning (no failure) ───

    [Fact]
    public async Task HandleAsync_DigestMismatch_RegistersWorkerSuccessfully()
    {
        // Arrange
        var handler = MakeHandler(out var repoMock, out _);

        repoMock.Setup(r => r.GetByIdAsync("w-digest", It.IsAny<CancellationToken>()))
                .ReturnsAsync((WorkerRecord?)null);

        repoMock.Setup(r => r.AddAsync(It.IsAny<WorkerRecord>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        var cmd = new RegisterWorkerCommand(
            WorkerId:               "w-digest",
            RuntimeType:            "Desktop",
            ActionNames:            ["ClickAction"],
            CapabilityDigest:       "intentionally-wrong-digest",
            AbsenceThresholdSeconds: 90);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert — registration succeeds despite the digest mismatch.
        result.IsSuccess.Should().BeTrue();
        result.Value.WorkerId.Should().Be("w-digest");

        // AddAsync called once (new worker was created).
        repoMock.Verify(r => r.AddAsync(It.IsAny<WorkerRecord>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── 5. Correct digest passes silently ─────────────────────────────────────

    [Fact]
    public async Task HandleAsync_CorrectDigest_RegistersSuccessfully()
    {
        // Arrange
        var handler = MakeHandler(out var repoMock, out _);

        repoMock.Setup(r => r.GetByIdAsync("w-ok-digest", It.IsAny<CancellationToken>()))
                .ReturnsAsync((WorkerRecord?)null);

        repoMock.Setup(r => r.AddAsync(It.IsAny<WorkerRecord>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        string[] actions  = ["ClickAction", "TypeAction"];
        var      digest   = RegisterWorkerCommandHandler.ComputeDigest(actions, "Desktop");

        var cmd = new RegisterWorkerCommand(
            WorkerId:               "w-ok-digest",
            RuntimeType:            "Desktop",
            ActionNames:            actions,
            CapabilityDigest:       digest,
            AbsenceThresholdSeconds: 120);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.WorkerId.Should().Be("w-ok-digest");
    }

    // ── 6. New worker State=AVAILABLE on creation ─────────────────────────────

    [Fact]
    public async Task HandleAsync_NewWorker_StateIsAvailableAndIsOnlineTrue()
    {
        // Arrange
        var handler = MakeHandler(out var repoMock, out _);

        repoMock.Setup(r => r.GetByIdAsync("w-state", It.IsAny<CancellationToken>()))
                .ReturnsAsync((WorkerRecord?)null);

        WorkerRecord? captured = null;
        repoMock.Setup(r => r.AddAsync(It.IsAny<WorkerRecord>(), It.IsAny<CancellationToken>()))
                .Callback<WorkerRecord, CancellationToken>((w, _) => captured = w)
                .Returns(Task.CompletedTask);

        var cmd = new RegisterWorkerCommand("w-state", "Browser", [], null, 60);

        // Act
        await handler.HandleAsync(cmd);

        // Assert
        captured.Should().NotBeNull();
        captured!.State.Should().Be(WorkerState.Available);
        captured.IsOnline.Should().BeTrue();
    }

    // ── 7. Re-registration on a RUNNING worker does NOT release it ───────────

    [Fact]
    public async Task HandleAsync_ReRegistrationWhileRunning_WorkerStaysRunning()
    {
        // Arrange
        var handler = MakeHandler(out var repoMock, out _);

        var runningWorker = new WorkerRecord("w-running", "Desktop", 60, FixedNow.AddMinutes(-10));
        runningWorker.MarkLeased();
        runningWorker.MarkRunning();

        repoMock.Setup(r => r.GetByIdAsync("w-running", It.IsAny<CancellationToken>()))
                .ReturnsAsync(runningWorker);

        repoMock.Setup(r => r.Update(It.IsAny<WorkerRecord>()));

        var cmd = new RegisterWorkerCommand("w-running", "Desktop", ["Action1"], null, 60);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        runningWorker.State.Should().Be(WorkerState.Running);
        repoMock.Verify(r => r.AddAsync(It.IsAny<WorkerRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
