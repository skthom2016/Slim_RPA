namespace Tracto.Orchestrator.Infrastructure.Clock;

/// <summary>
/// Production implementation of <see cref="IClock"/> backed by <see cref="DateTimeOffset.UtcNow"/>.
/// Register as a singleton in the DI container.
/// </summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
