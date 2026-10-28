namespace Tracto.Orchestrator.Application.Abstractions;

/// <summary>
/// Application-layer abstraction over the system clock.
/// Infrastructure provides <c>SystemClock</c>; tests use <c>FakeClock</c>.
/// All implementations must be thread-safe: UtcNow is read concurrently by async handlers.
/// </summary>
public interface IClock
{
    /// <summary>Returns the current UTC instant.</summary>
    DateTimeOffset UtcNow { get; }
}
