#nullable enable

namespace Tracto.Contract.Testing;

/// <summary>
/// A controllable <see cref="TimeProvider"/> for deterministic unit tests.
/// Replaces the real wall clock with a manually advanced clock so that
/// time-sensitive contract logic can be driven by the test.
/// </summary>
public sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    /// <param name="start">
    /// The initial value returned by <see cref="GetUtcNow"/>.
    /// Defaults to 2026-01-01T00:00:00Z when omitted.
    /// </param>
    public FakeTimeProvider(DateTimeOffset? start = null)
        => _now = start ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Advance the clock by <paramref name="by"/>.</summary>
    public void Advance(TimeSpan by) => _now = _now.Add(by);

    /// <summary>Jump the clock to an absolute point in time.</summary>
    public void SetTime(DateTimeOffset time) => _now = time;
}
