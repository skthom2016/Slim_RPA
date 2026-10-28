using System.Diagnostics;
using System.IO;

namespace Tracto.Desktop.AppLifecycle;

/// <summary>
/// Windows implementation of <see cref="IAppLifecycleManager"/> that uses
/// <see cref="System.Diagnostics.Process"/> to launch, attach and monitor processes.
/// </summary>
public sealed class WindowsAppLifecycleManager : IAppLifecycleManager
{
    // Injected factory lets tests substitute a fake process without P/Invoke.
    private readonly Func<string, string[], Process> _processFactory;

    /// <summary>Initialises with the production process factory.</summary>
    public WindowsAppLifecycleManager()
        : this(DefaultProcessFactory) { }

    /// <summary>Initialises with a custom process factory (for testing).</summary>
    public WindowsAppLifecycleManager(Func<string, string[], Process> processFactory)
    {
        _processFactory = processFactory ?? throw new ArgumentNullException(nameof(processFactory));
    }

    // ── LaunchAsync ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<AppHandle> LaunchAsync(AppLaunchOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        Process process;
        try
        {
            process = _processFactory(options.ExecutablePath, options.Args);
        }
        catch (Exception ex)
        {
            throw new DesktopActionError("ENVIRONMENT", "desktop.launchFailed",
                $"Failed to start process '{options.ExecutablePath}': {ex.Message}", ex);
        }

        // Wait for idle (GUI message queue drained) within the configured timeout.
        // Retry class is NEVER — we do not loop if WaitForInputIdle times out.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(options.LaunchTimeoutMs);

        try
        {
            await Task.Run(() => process.WaitForInputIdle(options.LaunchTimeoutMs), cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout branch — process may or may not be alive; outcome is uncertain.
            if (!process.HasExited)
            {
                return BuildHandle(process);
            }

            throw new DesktopActionError("ENVIRONMENT", "desktop.launchTimeout",
                $"Process '{options.ExecutablePath}' did not become idle within {options.LaunchTimeoutMs} ms.");
        }

        if (process.HasExited)
        {
            // Windows 11 packaged apps (e.g. modern Notepad) use a launcher stub: the stub
            // activates the real hosted process, then exits cleanly with code 0.
            // Detect this pattern and locate the actual process by executable base name.
            if (process.ExitCode == 0)
            {
                string targetName = Path.GetFileNameWithoutExtension(options.ExecutablePath);
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                    var alive = Process.GetProcessesByName(targetName)
                                       .Where(p => !p.HasExited)
                                       .ToArray();
                    if (alive.Length == 1)
                        return BuildHandle(alive[0]);
                    // 0 = not started yet; >1 = ambiguous; keep retrying up to 2 s.
                }
            }

            throw new DesktopActionError("ENVIRONMENT", "desktop.appCrash",
                $"Process '{options.ExecutablePath}' exited immediately after launch with code {process.ExitCode}.");
        }

        return BuildHandle(process);
    }

    // ── AttachAsync ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<AppHandle> AttachAsync(AppAttachOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.ProcessId is null && options.ProcessName is null)
        {
            throw new DesktopActionError("VALIDATION", null,
                "At least one of ProcessId or ProcessName must be supplied in AppAttachOptions.");
        }

        Process[] candidates;

        if (options.ProcessId is not null)
        {
            // PID is unique; resolve directly.
            try
            {
                var p = Process.GetProcessById(options.ProcessId.Value);
                candidates = [p];
            }
            catch (ArgumentException)
            {
                throw new DesktopActionError("TARGET_NOT_FOUND", null,
                    $"No process with PID {options.ProcessId.Value} is running.");
            }
        }
        else
        {
            // Name-based search — may return zero or multiple matches.
            candidates = Process.GetProcessesByName(options.ProcessName!);

            if (candidates.Length == 0)
            {
                throw new DesktopActionError("TARGET_NOT_FOUND", null,
                    $"No running process with name '{options.ProcessName}' was found.");
            }

            if (candidates.Length > 1)
            {
                throw new DesktopActionError("TARGET_AMBIGUOUS", null,
                    $"{candidates.Length} processes named '{options.ProcessName}' are running. " +
                    "Provide a ProcessId to disambiguate.");
            }
        }

        var process = candidates[0];

        if (process.HasExited)
        {
            throw new DesktopActionError("TARGET_NOT_FOUND", null,
                $"Process '{process.ProcessName}' (PID {process.Id}) has already exited.");
        }

        return Task.FromResult(BuildHandle(process));
    }

    // ── CloseAsync ──────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> CloseAsync(AppHandle handle, int gracefulTimeoutMs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);

        Process process;
        try
        {
            process = Process.GetProcessById(handle.ProcessId);
        }
        catch (ArgumentException)
        {
            // Already gone — treat as successfully closed.
            return true;
        }

        if (process.HasExited) return true;

        // Refresh is required: GetProcessById() returns a new Process object whose
        // MainWindowHandle is cached as IntPtr.Zero until Refresh() is called.
        // Without this, CloseMainWindow() silently sends WM_CLOSE to handle 0 (no-op).
        process.Refresh();

        bool sentClose = process.CloseMainWindow();

        bool exited = await Task.Run(() => process.WaitForExit(gracefulTimeoutMs), cancellationToken).ConfigureAwait(false);

        if (!exited)
        {
            // Graceful close did not succeed within the timeout (e.g. CloseMainWindow returned
            // false because no main window exists yet, or the process ignored WM_CLOSE).
            // Fall back to a forced kill so callers are not left with a zombie process.
            try
            {
                process.Kill();
                exited = true;
            }
            catch (InvalidOperationException)
            {
                // Process exited on its own between WaitForExit and Kill — treat as success.
                exited = true;
            }
        }

        _ = sentClose; // used above; suppress unused-variable warning
        return exited;
    }

    // ── MonitorForExit ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void MonitorForExit(AppHandle handle, Action<DesktopActionError> onExit)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(onExit);

        Process process;
        try
        {
            process = Process.GetProcessById(handle.ProcessId);
        }
        catch (ArgumentException)
        {
            // Process already gone — fire immediately.
            onExit(BuildCrashError(handle.ProcessName, null));
            return;
        }

        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            int? exitCode = null;
            try { exitCode = process.ExitCode; } catch { /* process may be disposed */ }
            onExit(BuildCrashError(handle.ProcessName, exitCode));
        };

        // Edge case: already exited between GetProcessById and Exited subscription.
        if (process.HasExited)
        {
            int? exitCode = null;
            try { exitCode = process.ExitCode; } catch { /* ignored */ }
            onExit(BuildCrashError(handle.ProcessName, exitCode));
        }
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    private static AppHandle BuildHandle(Process process)
    {
        process.Refresh();
        IntPtr hwnd = IntPtr.Zero;
        try { hwnd = process.MainWindowHandle; } catch { /* ignored */ }
        return new AppHandle(process.Id, process.ProcessName, hwnd);
    }

    private static DesktopActionError BuildCrashError(string processName, int? exitCode)
    {
        var detail = exitCode.HasValue
            ? $"Process '{processName}' exited unexpectedly with code {exitCode.Value}."
            : $"Process '{processName}' exited unexpectedly.";

        return new DesktopActionError("ENVIRONMENT", "desktop.appCrash", detail);
    }

    private static Process DefaultProcessFactory(string path, string[] args)
    {
        var info = new ProcessStartInfo(path)
        {
            // UseShellExecute=true lets the Windows shell activation path handle packaged
            // and hosted apps (e.g. Windows 11 Notepad) correctly. stdin/stdout redirection
            // is not needed for GUI automation targets.
            UseShellExecute = true,
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);

        return Process.Start(info)
               ?? throw new InvalidOperationException($"Process.Start returned null for '{path}'.");
    }
}
