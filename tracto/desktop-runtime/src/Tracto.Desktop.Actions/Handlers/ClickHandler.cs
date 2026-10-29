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
///   offset       (string, "x,y", optional) — click position relative to the window's top-left
///                corner; used instead of coordinates when the window is found
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
        var offsetRaw  = ctx.GetString("offset");
        var rightClick = ctx.GetBool("rightClick", false);

        // Activate target window first if specified.
        var windowFound = false;
        var windowLeft = 0;
        var windowTop = 0;
        if (!string.IsNullOrWhiteSpace(window))
        {
            var hwnd = NativeMethods.FindVisibleWindowByTitle(window);
            if (hwnd != IntPtr.Zero)
            {
                NativeMethods.SetForegroundWindow(hwnd);
                await Task.Delay(300, ctx.CancellationToken);
                windowFound = NativeMethods.TryGetWindowOrigin(hwnd, out windowLeft, out windowTop);
            }
            else
            {
                _logger.LogWarning("click: window '{Window}' not found — proceeding without activation", window);
            }
        }

        // Position relative to the window's top-left corner: stays correct when the window
        // (for example a Save As dialog) opens at a different place on screen.
        if (windowFound && TryParsePair(offsetRaw, out var offX, out var offY))
        {
            var cx = windowLeft + offX;
            var cy = windowTop + offY;
            if (rightClick) NativeMethods.RightClick(cx, cy); else NativeMethods.LeftClick(cx, cy);
            await Task.Delay(200, ctx.CancellationToken);
            _logger.LogInformation("click: window '{Window}' + offset ({OX},{OY}) → ({X},{Y}) {Kind}",
                window, offX, offY, cx, cy, rightClick ? "right" : "left");
            return ActionResult.Ok(new Dictionary<string, object?> { ["x"] = cx, ["y"] = cy });
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

    private static bool TryParsePair(string? raw, out int a, out int b)
    {
        a = b = 0;
        var parts = raw?.Split(',');
        return parts is { Length: >= 2 }
            && int.TryParse(parts[0].Trim(), out a)
            && int.TryParse(parts[1].Trim(), out b);
    }
}
