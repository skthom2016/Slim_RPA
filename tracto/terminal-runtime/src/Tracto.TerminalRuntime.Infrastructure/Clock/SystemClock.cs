using Tracto.TerminalRuntime.Domain;

namespace Tracto.TerminalRuntime.Infrastructure.Clock;

/// <summary>
/// Production <see cref="IClock"/> implementation backed by <see cref="DateTimeOffset.UtcNow"/>.
/// </summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc/>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
