using System.Windows;
using Tracto.Desktop.Element;

namespace Tracto.Desktop.Input;

/// <summary>
/// Production IMouseInputEngine. Uses an IElementBoundsProvider for UIA element location and
/// ISendInputProvider (thin P/Invoke wrapper) for actual input injection.
/// </summary>
public sealed class WindowsMouseInputEngine : IMouseInputEngine
{
    private readonly ISendInputProvider _sendInput;
    private readonly IClock _clock;
    private readonly IElementBoundsProvider _boundsProvider;

    public WindowsMouseInputEngine(
        ISendInputProvider sendInput,
        IClock clock,
        IElementBoundsProvider? boundsProvider = null)
    {
        _sendInput = sendInput ?? throw new ArgumentNullException(nameof(sendInput));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _boundsProvider = boundsProvider ?? UiaElementBoundsProvider.Instance;
    }

    // ─── Click ────────────────────────────────────────────────────────────────

    public Task ClickAsync(MouseClickOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ct.ThrowIfCancellationRequested();

        var bounds = _boundsProvider.GetBounds(options.Target);
        var (absX, absY) = ComputeAbsolutePoint(bounds, options.OffsetX, options.OffsetY);

        MoveCursor(absX, absY);

        var (downFlag, upFlag) = GetButtonFlags(options.Button);

        _sendInput.SendMouseInput(new[]
        {
            BuildAbsoluteMove(absX, absY),
            BuildButtonEvent(downFlag, 0),
            BuildButtonEvent(upFlag, 0)
        });

        return Task.CompletedTask;
    }

    // ─── Drag ─────────────────────────────────────────────────────────────────

    public async Task DragAsync(DragOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ct.ThrowIfCancellationRequested();

        var srcBounds = _boundsProvider.GetBounds(options.Source);
        var dstBounds = _boundsProvider.GetBounds(options.Target);
        var (srcX, srcY) = Center(srcBounds);
        var (dstX, dstY) = Center(dstBounds);

        MoveCursor(srcX, srcY);
        _sendInput.SendMouseInput(new[]
        {
            BuildAbsoluteMove(srcX, srcY),
            BuildButtonEvent(MouseEventFlags.LEFTDOWN, 0)
        });

        bool buttonReleased = false;
        try
        {
            int steps = Math.Max(1, options.DragDurationMs / 10);
            int delayPerStep = Math.Max(1, options.DragDurationMs / steps);
            for (int i = 1; i <= steps; i++)
            {
                ct.ThrowIfCancellationRequested();
                int x = srcX + (dstX - srcX) * i / steps;
                int y = srcY + (dstY - srcY) * i / steps;
                _sendInput.SendMouseInput(new[] { BuildAbsoluteMove(x, y) });
                await _clock.DelayAsync(delayPerStep, ct).ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();

            _sendInput.SendMouseInput(new[]
            {
                BuildAbsoluteMove(dstX, dstY),
                BuildButtonEvent(MouseEventFlags.LEFTUP, 0)
            });
            buttonReleased = true;

            // Drop outcome cannot be verified — per DT-08-AC06 / DT-08-AC04
            throw new UnknownOutcomeException("action.commitUncertain");
        }
        catch (OperationCanceledException) when (!buttonReleased)
        {
            // Safety: always release button before propagating (DT-08-AC08)
            _sendInput.SendMouseInput(new[] { BuildButtonEvent(MouseEventFlags.LEFTUP, 0) });
            throw;
        }
    }

    // ─── Scroll ───────────────────────────────────────────────────────────────

    public Task ScrollAsync(ScrollOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ct.ThrowIfCancellationRequested();

        var bounds = _boundsProvider.GetBounds(options.Target);
        var (absX, absY) = Center(bounds);
        MoveCursor(absX, absY);

        int delta = options.ScrollDown ? -120 * options.Clicks : 120 * options.Clicks;

        _sendInput.SendMouseInput(new[]
        {
            BuildAbsoluteMove(absX, absY),
            BuildWheelEvent((uint)Math.Abs(delta), delta < 0)
        });

        // Retry class NEVER — caller decides whether to scroll again.
        return Task.CompletedTask;
    }

    // ─── Hover ────────────────────────────────────────────────────────────────

    public async Task HoverAsync(HoverOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ct.ThrowIfCancellationRequested();

        var bounds = _boundsProvider.GetBounds(options.Target);
        var (absX, absY) = ComputeAbsolutePoint(bounds, options.OffsetX, options.OffsetY);

        MoveCursor(absX, absY);
        _sendInput.SendMouseInput(new[] { BuildAbsoluteMove(absX, absY) });

        if (options.DwellMs.HasValue && options.DwellMs.Value > 0)
        {
            await _clock.DelayAsync(options.DwellMs.Value, ct).ConfigureAwait(false);
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static (int x, int y) Center(Rect bounds)
        => ((int)(bounds.X + bounds.Width / 2), (int)(bounds.Y + bounds.Height / 2));

    private static (int x, int y) ComputeAbsolutePoint(Rect bounds, int? offsetX, int? offsetY)
    {
        var (cx, cy) = Center(bounds);
        int ox = offsetX ?? 0;
        int oy = offsetY ?? 0;

        // Guard: offset must stay within ±half-width and ±half-height (DT-08-AC02)
        if (Math.Abs(ox) > bounds.Width / 2)
            throw new ArgumentException(
                $"OffsetX {ox} exceeds half the element width ({bounds.Width / 2}).", nameof(offsetX));
        if (Math.Abs(oy) > bounds.Height / 2)
            throw new ArgumentException(
                $"OffsetY {oy} exceeds half the element height ({bounds.Height / 2}).", nameof(offsetY));

        return (cx + ox, cy + oy);
    }

    private static (uint down, uint up) GetButtonFlags(ClickButton button) => button switch
    {
        ClickButton.Left   => (MouseEventFlags.LEFTDOWN,   MouseEventFlags.LEFTUP),
        ClickButton.Right  => (MouseEventFlags.RIGHTDOWN,  MouseEventFlags.RIGHTUP),
        ClickButton.Middle => (MouseEventFlags.MIDDLEDOWN, MouseEventFlags.MIDDLEUP),
        _ => throw new ArgumentOutOfRangeException(nameof(button))
    };

    private static void MoveCursor(int x, int y)
    {
        NativeMethods.SetCursorPos(x, y);
    }

    private static MouseInput BuildAbsoluteMove(int x, int y)
    {
        int screenW = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        int screenH = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
        int nx = screenW > 0 ? x * 65535 / screenW : x;
        int ny = screenH > 0 ? y * 65535 / screenH : y;

        return new MouseInput
        {
            Type = 0,
            Dx = nx,
            Dy = ny,
            Flags = MouseEventFlags.MOVE | MouseEventFlags.ABSOLUTE
        };
    }

    private static MouseInput BuildButtonEvent(uint flags, uint mouseData) => new()
    {
        Type = 0,
        Flags = flags,
        MouseData = mouseData
    };

    private static MouseInput BuildWheelEvent(uint absDelta, bool scrollDown)
    {
        uint data = scrollDown
            ? unchecked((uint)(-((int)absDelta)))
            : absDelta;

        return new MouseInput
        {
            Type = 0,
            Flags = MouseEventFlags.WHEEL,
            MouseData = data
        };
    }
}
