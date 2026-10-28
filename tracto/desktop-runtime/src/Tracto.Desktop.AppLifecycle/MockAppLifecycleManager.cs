namespace Tracto.Desktop.AppLifecycle;

/// <summary>
/// Injectable test double for <see cref="IAppLifecycleManager"/>.
/// Records every call and returns pre-configured results.
/// </summary>
public sealed class MockAppLifecycleManager : IAppLifecycleManager
{
    // ── Configurable responses ───────────────────────────────────────────────

    /// <summary>Handle to return from <see cref="LaunchAsync"/>. Set before calling.</summary>
    public AppHandle? LaunchResult { get; set; }

    /// <summary>Exception to throw from <see cref="LaunchAsync"/>. Takes priority over <see cref="LaunchResult"/>.</summary>
    public Exception? LaunchException { get; set; }

    /// <summary>Handle to return from <see cref="AttachAsync"/>. Set before calling.</summary>
    public AppHandle? AttachResult { get; set; }

    /// <summary>Exception to throw from <see cref="AttachAsync"/>. Takes priority over <see cref="AttachResult"/>.</summary>
    public Exception? AttachException { get; set; }

    /// <summary>Value to return from <see cref="CloseAsync"/>.</summary>
    public bool CloseResult { get; set; } = true;

    // ── Call records ─────────────────────────────────────────────────────────

    public List<AppLaunchOptions> LaunchCalls { get; } = [];
    public List<AppAttachOptions> AttachCalls { get; } = [];
    public List<(AppHandle Handle, int GracefulTimeoutMs)> CloseCalls { get; } = [];
    public List<(AppHandle Handle, Action<DesktopActionError> OnExit)> MonitorCalls { get; } = [];

    // ── IAppLifecycleManager ─────────────────────────────────────────────────

    public Task<AppHandle> LaunchAsync(AppLaunchOptions options, CancellationToken cancellationToken = default)
    {
        LaunchCalls.Add(options);

        if (LaunchException is not null) throw LaunchException;

        return Task.FromResult(LaunchResult
               ?? throw new InvalidOperationException("MockAppLifecycleManager.LaunchResult is not set."));
    }

    public Task<AppHandle> AttachAsync(AppAttachOptions options, CancellationToken cancellationToken = default)
    {
        AttachCalls.Add(options);

        if (AttachException is not null) throw AttachException;

        return Task.FromResult(AttachResult
               ?? throw new InvalidOperationException("MockAppLifecycleManager.AttachResult is not set."));
    }

    public Task<bool> CloseAsync(AppHandle handle, int gracefulTimeoutMs, CancellationToken cancellationToken = default)
    {
        CloseCalls.Add((handle, gracefulTimeoutMs));
        return Task.FromResult(CloseResult);
    }

    public void MonitorForExit(AppHandle handle, Action<DesktopActionError> onExit)
    {
        MonitorCalls.Add((handle, onExit));
    }

    /// <summary>
    /// Simulates a process exit by invoking the last registered exit callback with
    /// <c>errorCategory=ENVIRONMENT</c>, <c>detailCode=desktop.appCrash</c>.
    /// </summary>
    public void SimulateProcessExit(string processName = "testapp", int exitCode = 1)
    {
        if (MonitorCalls.Count == 0)
            throw new InvalidOperationException("No MonitorForExit call has been registered.");

        var error = new DesktopActionError("ENVIRONMENT", "desktop.appCrash",
            $"Process '{processName}' exited unexpectedly with code {exitCode}.");

        MonitorCalls[^1].OnExit(error);
    }
}
