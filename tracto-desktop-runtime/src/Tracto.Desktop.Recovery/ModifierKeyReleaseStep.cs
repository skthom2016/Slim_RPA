using Tracto.Desktop.Input;

namespace Tracto.Desktop.Recovery;

/// <summary>
/// Release step that sends KEYEVENTF_KEYUP for every modifier key held at
/// the time of cancellation (DT-17-AC01).
/// </summary>
public sealed class ModifierKeyReleaseStep : IResourceReleaseStep
{
    private readonly IKeySendInputProvider _keyProvider;

    /// <param name="keyProvider">
    ///   Abstraction over Win32 SendInput keyboard events.
    /// </param>
    public ModifierKeyReleaseStep(IKeySendInputProvider keyProvider)
    {
        ArgumentNullException.ThrowIfNull(keyProvider);
        _keyProvider = keyProvider;
    }

    /// <inheritdoc/>
    public Task ExecuteAsync(CancellationContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        foreach (string key in ctx.HeldModifierKeys)
        {
            _keyProvider.KeyUp(key);
        }

        return Task.CompletedTask;
    }
}
