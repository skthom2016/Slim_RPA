using FluentAssertions;
using Tracto.Orchestrator.Domain.Workers;

namespace Tracto.Orchestrator.Unit.Tests.Domain;

/// <summary>
/// Unit tests for <see cref="WorkerRecord"/> domain entity (OC-15).
/// </summary>
public sealed class WorkerRecordTests
{
    private static readonly DateTimeOffset T0 =
        new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // ─── Constructor ──────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_ValidArgs_CreatesAvailableOnlineWorker()
    {
        var worker = new WorkerRecord("w-1", "Desktop", 120, T0);

        worker.WorkerId.Should().Be("w-1");
        worker.RuntimeType.Should().Be("Desktop");
        worker.State.Should().Be(WorkerState.Available);
        worker.IsOnline.Should().BeTrue();
        worker.RegisteredAt.Should().Be(T0);
        worker.LastHeartbeatAt.Should().Be(T0);
        worker.AbsenceThresholdSeconds.Should().Be(120);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankWorkerId_Throws(string workerId)
    {
        var act = () => new WorkerRecord(workerId, "Desktop", 120, T0);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankRuntimeType_Throws(string runtimeType)
    {
        var act = () => new WorkerRecord("w-1", runtimeType, 120, T0);
        act.Should().Throw<ArgumentException>();
    }

    // ─── MarkOffline ──────────────────────────────────────────────────────────

    [Fact]
    public void MarkOffline_OnlineWorker_BecomesOfflineAndNotOnline()
    {
        var worker = new WorkerRecord("w-offline", "Desktop", 120, T0);
        var t1 = T0.AddMinutes(5);

        worker.MarkOffline(t1);

        worker.IsOnline.Should().BeFalse();
        worker.State.Should().Be(WorkerState.Offline);
    }

    // ─── RecordHeartbeat ──────────────────────────────────────────────────────

    [Fact]
    public void RecordHeartbeat_OfflineWorker_BecomesOnlineAndAvailable()
    {
        var worker = new WorkerRecord("w-heartbeat", "Desktop", 120, T0);
        worker.MarkOffline(T0.AddMinutes(1));

        worker.RecordHeartbeat(T0.AddMinutes(2));

        worker.IsOnline.Should().BeTrue();
        worker.State.Should().Be(WorkerState.Available);
    }

    [Fact]
    public void RecordHeartbeat_OnlineWorker_UpdatesLastHeartbeat()
    {
        var worker = new WorkerRecord("w-beat", "Desktop", 120, T0);
        var t1 = T0.AddMinutes(2);

        worker.RecordHeartbeat(t1);

        worker.LastHeartbeatAt.Should().Be(t1);
        worker.IsOnline.Should().BeTrue();
    }

    // ─── MarkLeased / MarkRunning / MarkAvailable ─────────────────────────────

    [Fact]
    public void MarkLeased_AvailableWorker_BecomesLeased()
    {
        var worker = new WorkerRecord("w-lease", "Desktop", 120, T0);
        worker.MarkLeased();
        worker.State.Should().Be(WorkerState.Leased);
    }

    [Fact]
    public void MarkRunning_LeasedWorker_BecomesRunning()
    {
        var worker = new WorkerRecord("w-run", "Desktop", 120, T0);
        worker.MarkLeased();
        worker.MarkRunning();
        worker.State.Should().Be(WorkerState.Running);
    }

    [Fact]
    public void MarkAvailable_AfterRunning_BecomesAvailable()
    {
        var worker = new WorkerRecord("w-avail", "Desktop", 120, T0);
        worker.MarkLeased();
        worker.MarkRunning();
        worker.MarkAvailable();
        worker.State.Should().Be(WorkerState.Available);
    }

    // ─── IsOnline state ───────────────────────────────────────────────────────

    [Fact]
    public void IsOnline_AfterMarkOffline_IsFalse()
    {
        var worker = new WorkerRecord("w-check", "Desktop", 120, T0);
        worker.MarkOffline(T0.AddSeconds(1));
        worker.IsOnline.Should().BeFalse();
    }

    [Fact]
    public void AbsenceThresholdSeconds_StoredCorrectly()
    {
        var worker = new WorkerRecord("w-threshold", "Desktop", 300, T0);
        worker.AbsenceThresholdSeconds.Should().Be(300);
    }
}
