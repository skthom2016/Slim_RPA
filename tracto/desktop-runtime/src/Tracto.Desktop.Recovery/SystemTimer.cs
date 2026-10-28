namespace Tracto.Desktop.Recovery;

/// <summary>
/// Production implementation of <see cref="ITimer"/> backed by <see cref="Task.Delay(int, CancellationToken)"/>.
/// </summary>
public sealed class SystemTimer : ITimer
{
    /// <inheritdoc />
    public Task DelayAsync(int ms, CancellationToken ct)
        => Task.Delay(ms, ct);
}
