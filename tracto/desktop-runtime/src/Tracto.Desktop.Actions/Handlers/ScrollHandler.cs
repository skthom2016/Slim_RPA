#nullable enable
using Microsoft.Extensions.Logging;

namespace Tracto.Desktop.Actions.Handlers;

/// <summary>
/// Scrolls at a screen position using the mouse wheel.
///
/// Action name: scroll
/// Inputs:
///   coordinates  (string, "x,y") — screen coordinates to scroll at
///   delta        (int, optional, default 120) — wheel delta; positive = up, negative = down
///   window       (string, optional) — window title hint to activate first
/// </summary>
public sealed class ScrollHandler : IActionHandler
{
    private readonly ILogger<ScrollHandler> _logger;

    public string ActionName => "scroll";

    public ScrollHandler(ILogger<ScrollHandler> logger) => _logger = logger;

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx)
    {
        var window    = ctx.GetString("window");
        var coordsRaw = ctx.GetString("coordinates");
        var delta     = ctx.GetInt("delta", 120);

        if (!string.IsNullOrWhiteSpace(window))
        {
            var hwnd = NativeMethods.FindWindowByTitle(window);
            if (hwnd != IntPtr.Zero)
            {
                NativeMethods.SetForegroundWindow(hwnd);
                await Task.Delay(200, ctx.CancellationToken);
            }
        }

        if (string.IsNullOrWhiteSpace(coordsRaw))
        {
            _logger.LogWarning("scroll: no coordinates provided — no-op");
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

        NativeMethods.WheelScroll(x, y, delta);
        await Task.Delay(100, ctx.CancellationToken);

        _logger.LogInformation("scroll: ({X},{Y}) delta={Delta}", x, y, delta);
        return ActionResult.Ok();
    }
}
