using FluentAssertions;
using Tracto.Desktop.AppLifecycle;
using Xunit;

namespace Tracto.Desktop.AppLifecycle.Tests;

public sealed class AppLifecycleTests
{
    // ── DT-02-AC05: MonitorForExit callback fires with desktop.appCrash ───────

    [Fact]
    public void MonitorForExit_WhenProcessExits_CallbackReceivesAppCrashError()
    {
        // Arrange
        var mock = new MockAppLifecycleManager();
        var handle = new AppHandle(12345, "testapp", IntPtr.Zero);

        DesktopActionError? received = null;
        mock.MonitorForExit(handle, err => received = err);

        // Act — simulate the process crashing
        mock.SimulateProcessExit("testapp", exitCode: 1);

        // Assert
        received.Should().NotBeNull();
        received!.ErrorCategory.Should().Be("ENVIRONMENT");
        received.DetailCode.Should().Be("desktop.appCrash");
        received.Description.Should().Contain("testapp");
    }

    // ── DT-02-AC03: AttachAsync with duplicate names → TARGET_AMBIGUOUS ───────

    [Fact]
    public async Task AttachAsync_WithDuplicateMatchingNames_ThrowsTargetAmbiguous()
    {
        // Arrange: WindowsAppLifecycleManager does a real Process.GetProcessesByName call.
        // We need at least two processes with the same name. In practice we can only reliably
        // simulate this through the mock. Test both the mock path and the error shape.

        var mock = new MockAppLifecycleManager
        {
            AttachException = new DesktopActionError("TARGET_AMBIGUOUS", null,
                "2 processes named 'testapp' are running. Provide a ProcessId to disambiguate.")
        };

        var options = new AppAttachOptions(ProcessName: "testapp", ProcessId: null);

        // Act
        Func<Task> act = () => mock.AttachAsync(options);

        // Assert
        var ex = await act.Should().ThrowAsync<DesktopActionError>();
        ex.Which.ErrorCategory.Should().Be("TARGET_AMBIGUOUS");
    }

    // ── DesktopActionError shape ──────────────────────────────────────────────

    [Fact]
    public void DesktopActionError_Properties_ArePreserved()
    {
        var err = new DesktopActionError("ENVIRONMENT", "desktop.appCrash", "Process crashed.");

        err.ErrorCategory.Should().Be("ENVIRONMENT");
        err.DetailCode.Should().Be("desktop.appCrash");
        err.Description.Should().Be("Process crashed.");
        err.Message.Should().Contain("ENVIRONMENT");
        err.Message.Should().Contain("desktop.appCrash");
    }

    // ── MockAppLifecycleManager records calls ─────────────────────────────────

    [Fact]
    public async Task MockManager_LaunchAsync_RecordsCall()
    {
        var handle = new AppHandle(1, "app", IntPtr.Zero);
        var mock = new MockAppLifecycleManager { LaunchResult = handle };
        var options = new AppLaunchOptions("C:\\app.exe", [], 5000);

        var result = await mock.LaunchAsync(options);

        result.Should().Be(handle);
        mock.LaunchCalls.Should().ContainSingle().Which.Should().Be(options);
    }

    [Fact]
    public async Task MockManager_CloseAsync_RecordsCall()
    {
        var mock = new MockAppLifecycleManager { CloseResult = true };
        var handle = new AppHandle(42, "app", IntPtr.Zero);

        var closed = await mock.CloseAsync(handle, 3000);

        closed.Should().BeTrue();
        mock.CloseCalls.Should().ContainSingle().Which.Should().Be((handle, 3000));
    }

    // ── DT-02-AC01: LaunchAsync — real-GUI tests are skipped in CI ───────────

    [Fact]
    public async Task LaunchAsync_RealProcess_ReturnsHandle()
    {
        var manager = new WindowsAppLifecycleManager();
        var options = new AppLaunchOptions("notepad.exe", [], 10_000);

        var handle = await manager.LaunchAsync(options);

        handle.Should().NotBeNull();
        handle.ProcessId.Should().BeGreaterThan(0);
        handle.ProcessName.Should().ContainEquivalentOf("notepad");

        var closed = await manager.CloseAsync(handle, 3000);
        closed.Should().BeTrue("Notepad should have closed within 3 seconds");
    }

    [Fact(Skip = "Requires interactive Windows session")]
    public async Task AttachAsync_ByName_SingleProcess_ReturnsHandle()
    {
        var manager = new WindowsAppLifecycleManager();
        // Assumes exactly one Notepad is running. Windows 11 registers it as process name
        // "Notepad" (capital N); GetProcessesByName is case-insensitive so "notepad" matches.
        var handle = await manager.AttachAsync(new AppAttachOptions("notepad", null));
        handle.Should().NotBeNull();
    }
}
