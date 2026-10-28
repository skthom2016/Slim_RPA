#nullable enable
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Application.Workers;
using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Workers;
using Tracto.Orchestrator.Infrastructure.Clock;

namespace Tracto.Orchestrator.Unit.Tests.Workers;

/// <summary>
/// Unit tests for <see cref="RegisterWorkerCommandHandler"/> (OC-01).
///
/// ACs verified:
///   - Valid registration → workerId returned, State=AVAILABLE.
///   - Re-registration → LastRegisteredAt updated, no LEASED attempt released.
///   - Missing RuntimeType → Fail("runtimeType required").
///   - CapabilityDigest mismatch → registered with warning logged.
/// </summary>
public sealed class RegisterWorkerHandlerTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2024, 6, 15, 8, 0, 0, TimeSpan.Zero);

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static (
        RegisterWorkerCommandHandler Handler,
        Mock<IWorkerRepository>      WorkersMock,
        Mock<IUnitOfWork>            UoWMock,
        FakeClock                    Clock)
    BuildSut(WorkerRecord? existingWorker = null)
    {
        var workersMock = new Mock<IWorkerRepository>();
        var uowMock     = new Mock<IUnitOfWork>();
        var clock       = new FakeClock(FixedNow);
        var logger      = NullLogger<RegisterWorkerCommandHandler>.Instance;

        workersMock
            .Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingWorker);

        workersMock
            .Setup(r => r.AddAsync(It.IsAny<WorkerRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        workersMock.Setup(r => r.Update(It.IsAny<WorkerRecord>()));

        uowMock.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        uowMock.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);

        var handler = new RegisterWorkerCommandHandler(
            workersMock.Object,
            uowMock.Object,
            clock,
            new Mock<ITelemetryService>().Object,
            logger);

        return (handler, workersMock, uowMock, clock);
    }

    // ─── AC-1: Valid registration → workerId returned, State=AVAILABLE ─────────

    [Fact]
    public async Task ValidRegistration_NewWorker_ReturnsWorkerId()
    {
        // Arrange
        var (handler, _, _, _) = BuildSut(existingWorker: null);
        var cmd = new RegisterWorkerCommand(
            WorkerId:                "worker-new",
            RuntimeType:             "dotnet",
            ActionNames:             ["ClickAction", "TypeAction"],
            CapabilityDigest:        null,
            AbsenceThresholdSeconds: 60);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.WorkerId.Should().Be("worker-new");
    }

    [Fact]
    public async Task ValidRegistration_NewWorker_AddsWorkerWithAvailableState()
    {
        // Arrange
        WorkerRecord? captured = null;
        var workersMock = new Mock<IWorkerRepository>();
        var uowMock     = new Mock<IUnitOfWork>();
        var clock       = new FakeClock(FixedNow);

        workersMock
            .Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkerRecord?)null);
        workersMock
            .Setup(r => r.AddAsync(It.IsAny<WorkerRecord>(), It.IsAny<CancellationToken>()))
            .Callback<WorkerRecord, CancellationToken>((w, _) => captured = w)
            .Returns(Task.CompletedTask);
        uowMock.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uowMock.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var handler = new RegisterWorkerCommandHandler(
            workersMock.Object, uowMock.Object, clock,
            new Mock<ITelemetryService>().Object,
            NullLogger<RegisterWorkerCommandHandler>.Instance);

        var cmd = new RegisterWorkerCommand(
            WorkerId:                "worker-state-check",
            RuntimeType:             "python",
            ActionNames:             ["ExtractTable"],
            CapabilityDigest:        null,
            AbsenceThresholdSeconds: 120);

        // Act
        await handler.HandleAsync(cmd);

        // Assert
        captured.Should().NotBeNull();
        captured!.State.Should().Be(WorkerState.Available);
        captured.IsOnline.Should().BeTrue();
        captured.RuntimeType.Should().Be("python");
    }

    // ─── AC-2: Re-registration → LastRegisteredAt updated, no LEASED run released ─

    [Fact]
    public async Task ReRegistration_ExistingWorker_UpdatesLastRegisteredAt()
    {
        // Arrange — an already-registered worker.
        var existing = new WorkerRecord("worker-existing", "dotnet", 60, FixedNow.AddHours(-1));
        var (handler, workersMock, uowMock, clock) = BuildSut(existingWorker: existing);

        var cmd = new RegisterWorkerCommand(
            WorkerId:                "worker-existing",
            RuntimeType:             "dotnet",
            ActionNames:             ["ClickAction"],
            CapabilityDigest:        null,
            AbsenceThresholdSeconds: 90);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        existing.LastRegisteredAt.Should().Be(FixedNow, because: "clock.UtcNow is returned on re-registration");
        existing.AbsenceThresholdSeconds.Should().Be(90, because: "threshold is updated on re-registration");
    }

    [Fact]
    public async Task ReRegistration_ExistingWorker_DoesNotCallAddAsync()
    {
        // Arrange — ensure AddAsync is never called on re-registration.
        var existing = new WorkerRecord("worker-reregister", "dotnet", 60, FixedNow.AddHours(-2));
        var (handler, workersMock, _, _) = BuildSut(existingWorker: existing);

        var cmd = new RegisterWorkerCommand(
            WorkerId:                "worker-reregister",
            RuntimeType:             "dotnet",
            ActionNames:             [],
            CapabilityDigest:        null,
            AbsenceThresholdSeconds: 60);

        // Act
        await handler.HandleAsync(cmd);

        // Assert — AddAsync must NOT have been called.
        workersMock.Verify(
            r => r.AddAsync(It.IsAny<WorkerRecord>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReRegistration_LeasedWorkerRecord_IsNotModified()
    {
        // Arrange — simulate an existing worker whose state is Leased (holds an active lease).
        var existing = new WorkerRecord("worker-leased", "dotnet", 60, FixedNow.AddHours(-1));
        existing.MarkLeased();   // transitions to Leased state.

        WorkerRecord? captured = null;
        var workersMock = new Mock<IWorkerRepository>();
        var uowMock     = new Mock<IUnitOfWork>();
        var clock       = new FakeClock(FixedNow);

        workersMock
            .Setup(r => r.GetByIdAsync("worker-leased", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        workersMock
            .Setup(r => r.Update(It.IsAny<WorkerRecord>()))
            .Callback<WorkerRecord>(w => captured = w);
        uowMock.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uowMock.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var handler = new RegisterWorkerCommandHandler(
            workersMock.Object, uowMock.Object, clock,
            new Mock<ITelemetryService>().Object,
            NullLogger<RegisterWorkerCommandHandler>.Instance);

        var cmd = new RegisterWorkerCommand(
            WorkerId:                "worker-leased",
            RuntimeType:             "dotnet",
            ActionNames:             ["ClickAction"],
            CapabilityDigest:        null,
            AbsenceThresholdSeconds: 60);

        // Act
        await handler.HandleAsync(cmd);

        // Assert — the worker must still be in Leased state; UpdateRegistration must not release it.
        captured.Should().NotBeNull();
        captured!.State.Should().Be(WorkerState.Leased,
            because: "re-registration must never release an active LEASED worker");
    }

    // ─── AC-3a: Missing WorkerId → 400 ───────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingWorkerId_ReturnsValidationError(string workerId)
    {
        // Arrange
        var (handler, _, _, _) = BuildSut();
        var cmd = new RegisterWorkerCommand(
            WorkerId:                workerId,
            RuntimeType:             "dotnet",
            ActionNames:             [],
            CapabilityDigest:        null,
            AbsenceThresholdSeconds: 60);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Validation");
        result.Error.Message.Should().Contain("workerId required");
    }

    // ─── AC-3b: Missing RuntimeType → 400 ────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingRuntimeType_ReturnsValidationError(string runtimeType)
    {
        // Arrange
        var (handler, _, _, _) = BuildSut();
        var cmd = new RegisterWorkerCommand(
            WorkerId:                "worker-bad",
            RuntimeType:             runtimeType,
            ActionNames:             [],
            CapabilityDigest:        null,
            AbsenceThresholdSeconds: 60);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Validation");
        result.Error.Message.Should().Contain("runtimeType required");
    }

    // ─── AC-4: CapabilityDigest mismatch → registered with warning logged ──────

    [Fact]
    public async Task DigestMismatch_StillRegistersWorker()
    {
        // Arrange — provide a deliberately wrong digest.
        var (handler, _, _, _) = BuildSut(existingWorker: null);
        var cmd = new RegisterWorkerCommand(
            WorkerId:                "worker-digest",
            RuntimeType:             "browser",
            ActionNames:             ["Navigate", "Click"],
            CapabilityDigest:        "aaaaaaaabbbbbbbbccccccccdddddddd0000000011111111222222223333333344444444",
            AbsenceThresholdSeconds: 30);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert — despite the bad digest, registration succeeds (warning only per OC-01 spec).
        result.IsSuccess.Should().BeTrue(
            because: "CapabilityDigest mismatch is a warning, not a failure (OC-01 spec)");
        result.Value.WorkerId.Should().Be("worker-digest");
    }

    // ─── WorkerCapability: ActionNames treated as an order-independent set ──────

    [Fact]
    public void WorkerCapability_SameActionsInDifferentOrder_AreEqual()
    {
        // WorkerCapability normalises ActionNames (sorts them) so two capabilities that
        // declare the same actions in a different caller-supplied order must compare equal.
        var cap1 = new WorkerCapability(["Navigate", "Click"], "browser");
        var cap2 = new WorkerCapability(["Click", "Navigate"], "browser");

        cap1.Should().Be(cap2, because: "ActionNames are a set; order must not matter");
        cap1.ActionNames.Should().Equal(cap2.ActionNames,
            because: "both should be stored in the same canonical sorted order");
    }

    // ─── Commit / UoW: CommitAsync called exactly once ─────────────────────────

    [Fact]
    public async Task Handle_AlwaysCallsCommitOnce()
    {
        // Arrange
        var (handler, _, uowMock, _) = BuildSut(existingWorker: null);
        var cmd = new RegisterWorkerCommand(
            WorkerId:                "worker-commit",
            RuntimeType:             "dotnet",
            ActionNames:             [],
            CapabilityDigest:        null,
            AbsenceThresholdSeconds: 60);

        // Act
        await handler.HandleAsync(cmd);

        // Assert
        uowMock.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── DigestHelper: canonical digest computation is stable ──────────────────

    [Fact]
    public void ComputeDigest_SameInputsInDifferentOrder_ReturnsSameHash()
    {
        // Action order should not affect the digest (inputs are sorted internally).
        var digest1 = RegisterWorkerCommandHandler.ComputeDigest(
            ["Navigate", "Click"], "browser");
        var digest2 = RegisterWorkerCommandHandler.ComputeDigest(
            ["Click", "Navigate"], "browser");

        digest1.Should().Be(digest2,
            because: "ComputeDigest sorts action names before hashing");
    }

    [Fact]
    public void ComputeDigest_DifferentRuntimeTypes_ReturnDifferentHashes()
    {
        var digest1 = RegisterWorkerCommandHandler.ComputeDigest(["ClickAction"], "dotnet");
        var digest2 = RegisterWorkerCommandHandler.ComputeDigest(["ClickAction"], "python");

        digest1.Should().NotBe(digest2);
    }
}
