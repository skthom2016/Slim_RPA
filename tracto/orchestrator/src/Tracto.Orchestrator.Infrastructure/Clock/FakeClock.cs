namespace Tracto.Orchestrator.Infrastructure.Clock;

/// <summary>
/// Deterministic <see cref="IClock"/> implementation for unit and integration tests.
/// Time stands still at the initially supplied instant until <see cref="Advance"/> is called.
/// All operations are thread-safe.
/// </summary>
public sealed class FakeClock : IClock
{
    private DateTimeOffset _now;

    /// <summary>
    /// Initialises the clock at the given instant.
    /// Defaults to 2024-01-01 00:00:00 UTC when no argument is supplied so tests are deterministic
    /// even without an explicit seed.
    /// </summary>
    public FakeClock(DateTimeOffset? seed = null)
    {
        _now = seed ?? new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    /// <inheritdoc />
    public DateTimeOffset UtcNow { get { lock (this) { return _now; } } }

    /// <summary>Moves the clock forward by <paramref name="duration"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="duration"/> is negative.</exception>
    public void Advance(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), "FakeClock cannot go backwards.");

        lock (this) { _now = _now.Add(duration); }
    }

    /// <summary>Teleports the clock to an absolute instant.</summary>
    public void Set(DateTimeOffset instant)
    {
        lock (this) { _now = instant; }
    }
}
