#nullable enable
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;

namespace Tracto.Desktop.Actions.Handlers;

/// <summary>
/// Types text into Notepad using the clipboard-paste strategy.
/// Finds the Notepad window, sets clipboard text, brings window to foreground,
/// then pastes with Ctrl+V.
/// Action name: notepad.type
/// Inputs:
///   text        (string, required) — text to paste into Notepad
///   windowTitle (string, optional, default "Notepad") — partial title to match
/// Outputs:
///   charactersTyped (int) — length of text pasted
/// </summary>
public sealed class NotepadTypeHandler : IActionHandler
{
    private readonly ILogger<NotepadTypeHandler> _logger;

    public string ActionName => "notepad.type";

    public NotepadTypeHandler(ILogger<NotepadTypeHandler> logger) => _logger = logger;

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx)
    {
        var text = ctx.GetString("text");
        if (text is null)
            return ActionResult.Fail("CONFIGURATION", "Input 'text' is required");

        var titleHint = ctx.GetString("windowTitle") ?? "Notepad";

        var hwnd = FindNotepadWindow(titleHint);
        if (hwnd == IntPtr.Zero)
            return ActionResult.Fail("NOT_FOUND", $"No window with title containing '{titleHint}' found");

        NativeMethods.SetForegroundWindow(hwnd);
        await Task.Delay(300, ctx.CancellationToken);

        // Clipboard must be set on an STA thread
        var clipboardSet = await Task.Factory.StartNew(() =>
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch
            {
                return false;
            }
        }, ctx.CancellationToken, TaskCreationOptions.None, SynchronizationContext.Current is null
            ? TaskScheduler.Default
            : TaskScheduler.FromCurrentSynchronizationContext());

        if (!clipboardSet)
            return ActionResult.Fail("EXECUTION_FAILED", "Failed to set clipboard text");

        await Task.Delay(100, ctx.CancellationToken);

        // Ctrl+V to paste
        SendKeys.SendWait("^v");
        await Task.Delay(300, ctx.CancellationToken);

        _logger.LogInformation("notepad.type pasted {Chars} characters into '{Title}'", text.Length, titleHint);

        return ActionResult.Ok(new Dictionary<string, object?>
        {
            ["charactersTyped"] = text.Length
        });
    }

    private static IntPtr FindNotepadWindow(string titleHint)
    {
        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            var buf = new System.Text.StringBuilder(512);
            NativeMethods.GetWindowText(hwnd, buf, buf.Capacity);
            if (buf.ToString().Contains(titleHint, StringComparison.OrdinalIgnoreCase))
            {
                found = hwnd;
                return false; // stop enumeration
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
