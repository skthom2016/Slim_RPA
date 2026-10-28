using Tracto.Desktop.Input;

namespace Tracto.Desktop.Recovery;

/// <summary>
/// Release step that sends MOUSEEVENTF_LEFTUP when the left mouse button is
/// logically held at the time of cancellation (DT-17-AC02).
/// </summary>
public sealed class MouseButtonReleaseStep : IResourceReleaseStep
{
    private readonly ISendInputProvider _mouseProvider;

    /// <param name="mouseProvider">
    ///   Abstraction over Win32 SendInput mouse events.
    /// </param>
    public MouseButtonReleaseStep(ISendInputProvider mouseProvider)
    {
        ArgumentNullException.ThrowIfNull(mouseProvider);
        _mouseProvider = mouseProvider;
    }

    /// <inheritdoc/>
    public Task ExecuteAsync(CancellationContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        if (ctx.IsMouseButtonDown)
        {
            _mouseProvider.SendMouseInput(new[]
            {
                new MouseInput
                {
                    Type      = 0,                         // INPUT_MOUSE
                    Flags     = MouseEventFlags.LEFTUP,
                    Dx        = 0,
                    Dy        = 0,
                    MouseData = 0,
                    Time      = 0,
                    ExtraInfo = IntPtr.Zero
                }
            });
        }

        return Task.CompletedTask;
    }
}
