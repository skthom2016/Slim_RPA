#nullable enable
using Microsoft.Extensions.Logging;

namespace Tracto.Desktop.Actions.Handlers;

/// <summary>
/// Moves the mouse cursor to recorded screen coordinates and left-clicks,
/// optionally bringing a window to the foreground first.
///
/// Action name: click
/// Inputs:
///   coordinates  (string, "x,y") — screen coordinates recorded by the capture engine
///   window       (string, optional) — window title hint to activate before clicking
///   rightClick   (bool, optional, default false) — right-click instead of left-click
/// Outputs:
///   x (int), y (int) — coordinates where the click landed
/// </summary>
public sealed class ClickHandler : IActionHandler
{
    private readonly ILogger<ClickHandler> _logger;

    public string ActionName => "click";

    public ClickHandler(ILogger<ClickHandler> logger) => _logger = logger;

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx)
    {
        var window     = ctx.GetString("window");
        var coordsRaw  = ctx.GetString("coordinates");
        var rightClick = ctx.GetBool("rightClick", false);

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
                _logger.LogWarning("click: window '{Window}' not found — proceeding without activation", window);
            }
        }

        if (string.IsNullOrWhiteSpace(coordsRaw))
        {
            // No coordinates — nothing to click. Log and treat as a no-op so the run continues.
            _logger.LogWarning("click: no coordinates provided (target={Target}) — skipping physical click",
                ctx.GetString("target") ?? "(none)");
            return ActionResult.Ok();
        }

        var parts = coordsRaw.Split(',');
        if (parts.Length < 2 ||
            !int.TryParse(parts[0].Trim(), out var x) ||
            !int.TryParse(parts[1].Trim(), out var y))
        {
            return ActionResult.Fail("CONFIGURATION",
                $"Invalid coordinates '{coordsRaw}' — expected 'x,y' integers");
        }

        if (rightClick)
            NativeMethods.RightClick(x, y);
        else
            NativeMethods.LeftClick(x, y);

        await Task.Delay(200, ctx.CancellationToken);

        _logger.LogInformation("click: ({X},{Y}) {Kind}", x, y, rightClick ? "right" : "left");

        return ActionResult.Ok(new Dictionary<string, object?> { ["x"] = x, ["y"] = y });
    }
}
