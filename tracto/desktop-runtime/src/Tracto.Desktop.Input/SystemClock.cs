namespace Tracto.Desktop.Input;

/// <summary>
/// Production IClock that delegates to Task.Delay.
/// </summary>
public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    public Task DelayAsync(int ms, CancellationToken ct = default)
        => Task.Delay(ms, ct);
}
