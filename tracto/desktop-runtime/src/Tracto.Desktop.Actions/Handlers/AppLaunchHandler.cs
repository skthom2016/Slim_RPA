#nullable enable
using Microsoft.Extensions.Logging;
using Tracto.Desktop.AppLifecycle;

namespace Tracto.Desktop.Actions.Handlers;

/// <summary>
/// Launches a Windows application or attaches to one that is already running.
/// Action name: app.launch
/// Inputs:
///   executable      (string, required) — process name or full path (e.g. "notepad" or "C:\Windows\notepad.exe")
///   args            (string, optional) — space-separated command-line arguments
///   timeoutMs       (int, optional, default 10000) — max wait for the process to become idle
///   attachIfRunning (bool, optional, default false) — if true, try attaching before launching
/// Outputs:
///   processId   (int)    — OS process ID
///   windowTitle (string) — main window title (empty if no window)
/// </summary>
public sealed class AppLaunchHandler : IActionHandler
{
    private readonly IAppLifecycleManager _lifecycle;
    private readonly ILogger<AppLaunchHandler> _logger;

    public string ActionName => "app.launch";

    public AppLaunchHandler(IAppLifecycleManager lifecycle, ILogger<AppLaunchHandler> logger)
    {
        _lifecycle = lifecycle;
        _logger    = logger;
    }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx)
    {
        var executable = ctx.GetString("executable");
        if (string.IsNullOrWhiteSpace(executable))
            return ActionResult.Fail("CONFIGURATION", "Input 'executable' is required");

        var timeoutMs       = ctx.GetInt("timeoutMs", 10000);
        var attachIfRunning = ctx.GetBool("attachIfRunning", false);
        var argsRaw         = ctx.GetString("args") ?? string.Empty;
        var args            = string.IsNullOrWhiteSpace(argsRaw)
            ? Array.Empty<string>()
            : argsRaw.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        AppHandle handle;

        if (attachIfRunning)
        {
            try
            {
                var processName = Path.GetFileNameWithoutExtension(executable);
                handle = await _lifecycle.AttachAsync(
                    new AppAttachOptions(processName, null),
                    ctx.CancellationToken);
                _logger.LogInformation("Attached to existing process '{Process}' (pid={Pid})", processName, handle.ProcessId);
            }
            catch
            {
                _logger.LogDebug("Attach failed, launching '{Executable}'", executable);
                handle = await _lifecycle.LaunchAsync(
                    new AppLaunchOptions(executable, args, timeoutMs),
                    ctx.CancellationToken);
            }
        }
        else
        {
            handle = await _lifecycle.LaunchAsync(
                new AppLaunchOptions(executable, args, timeoutMs),
                ctx.CancellationToken);
        }

        _logger.LogInformation("app.launch succeeded: pid={Pid}, name={Name}", handle.ProcessId, handle.ProcessName);

        var windowTitle = string.Empty;
        if (handle.MainWindowHandle != IntPtr.Zero)
        {
            var buf = new System.Text.StringBuilder(512);
            NativeMethods.GetWindowText(handle.MainWindowHandle, buf, buf.Capacity);
            windowTitle = buf.ToString();
        }

        return ActionResult.Ok(new Dictionary<string, object?>
        {
            ["processId"]   = handle.ProcessId,
            ["windowTitle"] = windowTitle
        });
    }
}
