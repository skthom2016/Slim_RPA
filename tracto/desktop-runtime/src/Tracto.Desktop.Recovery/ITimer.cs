namespace Tracto.Desktop.Recovery;

/// <summary>
/// Abstraction over async delays, enabling deterministic testing via <see cref="FakeTimer"/>.
/// </summary>
public interface ITimer
{
    /// <summary>
    /// Waits for <paramref name="ms"/> milliseconds (or until <paramref name="ct"/> is cancelled).
    /// </summary>
    Task DelayAsync(int ms, CancellationToken ct);
}
