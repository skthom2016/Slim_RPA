namespace Tracto.Desktop.Input;

/// <summary>
/// Abstraction for async time delays, enabling deterministic testing via FakeClock.
/// </summary>
public interface IClock
{
    /// <summary>
    /// Delays for the specified number of milliseconds, respecting cancellation.
    /// </summary>
    Task DelayAsync(int ms, CancellationToken ct = default);
}
