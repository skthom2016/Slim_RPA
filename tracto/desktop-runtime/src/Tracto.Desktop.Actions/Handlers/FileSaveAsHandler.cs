#nullable enable
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;

namespace Tracto.Desktop.Actions.Handlers;

/// <summary>
/// Saves a document via the host application's Save As dialog.
/// Sends Ctrl+S to the target window, waits for the Save As dialog, types the file path, presses Enter.
/// Action name: file.saveAs
/// Inputs:
///   filePath    (string, required) — full destination path including filename and extension
///   windowTitle    (string, optional, default "Notepad") — window to initiate save from
///   overwrite      (bool, optional, default true) — dismiss "file already exists" confirmation
///   saveAsShortcut (string, optional, default "^s") — SendKeys string to open Save As dialog.
///                   Use "{F12}" for Excel (opens traditional dialog instead of backstage view).
/// Outputs:
///   savedPath      (string) — actual path saved to
///   fileSizeBytes  (long)   — file size after save (0 if file not found within timeout)
/// </summary>
public sealed class FileSaveAsHandler : IActionHandler
{
    private readonly ILogger<FileSaveAsHandler> _logger;

    public string ActionName => "file.saveAs";

    public FileSaveAsHandler(ILogger<FileSaveAsHandler> logger) => _logger = logger;

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx)
    {
        var filePath       = ctx.GetString("filePath");
        var windowTitle    = ctx.GetString("windowTitle") ?? "Notepad";
        var overwrite      = ctx.GetBool("overwrite", true);
        // saveAsShortcut: SendKeys string to open Save As — defaults to ^s (Ctrl+S).
        // Use "{F12}" for Excel, which opens the traditional Save As dialog instead of backstage.
        var saveAsShortcut = ctx.GetString("saveAsShortcut") ?? "^s";

        if (string.IsNullOrWhiteSpace(filePath))
            return ActionResult.Fail("CONFIGURATION", "Input 'filePath' is required");

        // Expand environment variables in path (e.g. %USERPROFILE%)
        var expandedPath = Environment.ExpandEnvironmentVariables(filePath);

        var hwnd = FindWindowByTitle(windowTitle);
        if (hwnd == IntPtr.Zero)
            return ActionResult.Fail("NOT_FOUND", $"No window with title containing '{windowTitle}' found");

        NativeMethods.SetForegroundWindow(hwnd);
        await Task.Delay(300, ctx.CancellationToken);

        SendKeys.SendWait(saveAsShortcut);
        await Task.Delay(800, ctx.CancellationToken);

        // Wait for a "Save As" dialog to appear (max 5s)
        var saveAsHwnd = await WaitForWindow("Save As", 5000, ctx.CancellationToken);
        if (saveAsHwnd == IntPtr.Zero)
        {
            // May have saved directly (file already had a name). Check if overwrite dialog appeared.
            var overwriteHwnd = FindWindowByTitle("Notepad");
            _logger.LogWarning("Save As dialog did not appear; file may have been saved directly");
        }
        else
        {
            NativeMethods.SetForegroundWindow(saveAsHwnd);
            await Task.Delay(300, ctx.CancellationToken);

            // Select all text in the filename field and replace with our path
            SendKeys.SendWait("^a");
            await Task.Delay(100, ctx.CancellationToken);
            SendKeys.SendWait("{DELETE}");
            await Task.Delay(100, ctx.CancellationToken);

            // Type the path (escape special SendKeys characters)
            SendKeys.SendWait(EscapeForSendKeys(expandedPath));
            await Task.Delay(200, ctx.CancellationToken);

            // Confirm
            SendKeys.SendWait("{ENTER}");
            await Task.Delay(800, ctx.CancellationToken);

            // Dismiss "File already exists?" if it appears
            if (overwrite)
            {
                var confirmHwnd = FindWindowByTitle("Confirm Save As");
                if (confirmHwnd != IntPtr.Zero)
                {
                    NativeMethods.SetForegroundWindow(confirmHwnd);
                    await Task.Delay(200, ctx.CancellationToken);
                    SendKeys.SendWait("{ENTER}");
                    await Task.Delay(500, ctx.CancellationToken);
                }
            }
        }

        await Task.Delay(500, ctx.CancellationToken);

        long fileSize = 0;
        if (File.Exists(expandedPath))
            fileSize = new FileInfo(expandedPath).Length;

        _logger.LogInformation("file.saveAs completed: {Path} ({Bytes} bytes)", expandedPath, fileSize);

        return ActionResult.Ok(new Dictionary<string, object?>
        {
            ["savedPath"]     = expandedPath,
            ["fileSizeBytes"] = fileSize
        });
    }

    private static IntPtr FindWindowByTitle(string titleHint)
    {
        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            var buf = new System.Text.StringBuilder(512);
            NativeMethods.GetWindowText(hwnd, buf, buf.Capacity);
            if (buf.ToString().Contains(titleHint, StringComparison.OrdinalIgnoreCase))
            {
                found = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static async Task<IntPtr> WaitForWindow(string titleHint, int timeoutMs, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var hwnd = FindWindowByTitle(titleHint);
            if (hwnd != IntPtr.Zero) return hwnd;
            await Task.Delay(200, ct);
        }
        return IntPtr.Zero;
    }

    private static string EscapeForSendKeys(string text)
    {
        // SendKeys treats +, ^, %, ~, (, ), {, } as special characters
        var sb = new System.Text.StringBuilder(text.Length * 2);
        foreach (var ch in text)
        {
            if ("(){}[]^%~+".Contains(ch))
                sb.Append('{').Append(ch).Append('}');
            else
                sb.Append(ch);
        }
        return sb.ToString();
    }
}
