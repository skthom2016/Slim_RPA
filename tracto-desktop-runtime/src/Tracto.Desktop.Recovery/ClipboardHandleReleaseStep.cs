using Tracto.Desktop.Input;

namespace Tracto.Desktop.Recovery;

/// <summary>
/// Release step that closes an open clipboard handle via
/// <see cref="IClipboardEngine.ForceCloseHandle"/> when the clipboard was open
/// at the time of cancellation (DT-17-AC03).
/// </summary>
public sealed class ClipboardHandleReleaseStep : IResourceReleaseStep
{
    private readonly IClipboardEngine _clipboardEngine;

    /// <param name="clipboardEngine">
    ///   Clipboard engine whose <see cref="IClipboardEngine.ForceCloseHandle"/>
    ///   method is called unconditionally when a handle is open.
    /// </param>
    public ClipboardHandleReleaseStep(IClipboardEngine clipboardEngine)
    {
        ArgumentNullException.ThrowIfNull(clipboardEngine);
        _clipboardEngine = clipboardEngine;
    }

    /// <inheritdoc/>
    public Task ExecuteAsync(CancellationContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        if (ctx.ClipboardHandleOpen)
        {
            _clipboardEngine.ForceCloseHandle();
        }

        return Task.CompletedTask;
    }
}
