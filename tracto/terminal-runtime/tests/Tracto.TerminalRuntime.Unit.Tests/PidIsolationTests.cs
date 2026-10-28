using FluentAssertions;
using Moq;
using Tracto.TerminalRuntime.Application;
using Tracto.TerminalRuntime.Infrastructure;

namespace Tracto.TerminalRuntime.Unit.Tests;

public sealed class PidIsolationTests
{
    // -----------------------------------------------------------------------
    // Tests using the real ProcessTracker implementation
    // -----------------------------------------------------------------------

    [Fact]
    public void Track_Pid_IsOwnedByCorrectJob()
    {
        var tracker = new ProcessTracker();
        tracker.Track("job-A", 1001);

        tracker.IsOwnedByJob("job-A", 1001).Should().BeTrue();
    }

    [Fact]
    public void Track_Pid_IsNotOwnedByOtherJob()
    {
        var tracker = new ProcessTracker();
        tracker.Track("job-A", 1001);

        tracker.IsOwnedByJob("job-B", 1001).Should().BeFalse(
            "a PID registered under job-A must never appear as owned by job-B");
    }

    [Fact]
    public void TwoWorkers_SameHost_PidSetsDontOverlap()
    {
        var tracker = new ProcessTracker();
        tracker.Track("worker-1-job-X", 2000);
        tracker.Track("worker-1-job-X", 2001);
        tracker.Track("worker-2-job-Y", 3000);
        tracker.Track("worker-2-job-Y", 3001);

        tracker.IsOwnedByJob("worker-1-job-X", 2000).Should().BeTrue();
        tracker.IsOwnedByJob("worker-1-job-X", 2001).Should().BeTrue();
        tracker.IsOwnedByJob("worker-2-job-Y", 2000).Should().BeFalse();
        tracker.IsOwnedByJob("worker-2-job-Y", 2001).Should().BeFalse();

        tracker.IsOwnedByJob("worker-2-job-Y", 3000).Should().BeTrue();
        tracker.IsOwnedByJob("worker-2-job-Y", 3001).Should().BeTrue();
        tracker.IsOwnedByJob("worker-1-job-X", 3000).Should().BeFalse();
        tracker.IsOwnedByJob("worker-1-job-X", 3001).Should().BeFalse();
    }

    [Fact]
    public void Untrack_Pid_IsNoLongerOwned()
    {
        var tracker = new ProcessTracker();
        tracker.Track("job-A", 1001);
        tracker.Untrack("job-A", 1001);

        tracker.IsOwnedByJob("job-A", 1001).Should().BeFalse();
    }

    [Fact]
    public void GetPids_ReturnsAllPidsForJob()
    {
        var tracker = new ProcessTracker();
        tracker.Track("job-A", 100);
        tracker.Track("job-A", 200);

        var pids = tracker.GetPids("job-A");
        pids.Should().BeEquivalentTo(new[] { 100, 200 });
    }

    [Fact]
    public void GetPids_UnknownJob_ReturnsEmpty()
    {
        var tracker = new ProcessTracker();

        tracker.GetPids("non-existent-job").Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Tests using a mock IProcessTracker (verifying consumer contract)
    // -----------------------------------------------------------------------

    [Fact]
    public void MockTracker_Track_IsCalled_WithCorrectArguments()
    {
        var mock = new Mock<IProcessTracker>();
        mock.Setup(t => t.IsOwnedByJob("job-Z", 9999)).Returns(true);

        mock.Object.Track("job-Z", 9999);

        mock.Verify(t => t.Track("job-Z", 9999), Times.Once);
    }

    [Fact]
    public void MockTracker_IsOwnedByJob_ReturnsFalse_ForOtherJob()
    {
        var mock = new Mock<IProcessTracker>();
        mock.Setup(t => t.IsOwnedByJob("job-A", 1234)).Returns(true);
        mock.Setup(t => t.IsOwnedByJob("job-B", 1234)).Returns(false);

        mock.Object.IsOwnedByJob("job-B", 1234).Should().BeFalse();
    }
}
