#nullable enable
using System.Windows.Forms;
using Microsoft.Extensions.Logging;

namespace Tracto.Desktop.Actions.Handlers;

/// <summary>
/// Types text into the currently active window (or a window matched by title)
/// using SendKeys, which simulates keystrokes at the OS level.
///
/// Action name: type
/// Inputs:
///   value   (string, required) — text to type
///   window  (string, optional) — window title hint to activate before typing
/// Outputs:
///   charactersTyped (int)
/// </summary>
public sealed class TypeHandler : IActionHandler
{
    private readonly ILogger<TypeHandler> _logger;

    public string ActionName => "type";

    public TypeHandler(ILogger<TypeHandler> logger) => _logger = logger;

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx)
    {
        var value  = ctx.GetString("value");
        var window = ctx.GetString("window");

        if (value is null)
            return ActionResult.Fail("CONFIGURATION", "Input 'value' is required for type action");

        // Activate target window first if specified.
        if (!string.IsNullOrWhiteSpace(window))
        {
            var hwnd = NativeMethods.FindWindowByTitle(window);
            if (hwnd != IntPtr.Zero)
            {
                NativeMethods.SetForegroundWindow(hwnd);
                await Task.Delay(300, ctx.CancellationToken);
            }
            else
            {
                _logger.LogWarning("type: window '{Window}' not found — typing into active window", window);
            }
        }

        await Task.Delay(100, ctx.CancellationToken);
        SendKeys.SendWait(NativeMethods.EscapeForSendKeys(value));
        await Task.Delay(150, ctx.CancellationToken);

        _logger.LogInformation("type: typed {Count} character(s)", value.Length);

        return ActionResult.Ok(new Dictionary<string, object?> { ["charactersTyped"] = value.Length });
    }
}
