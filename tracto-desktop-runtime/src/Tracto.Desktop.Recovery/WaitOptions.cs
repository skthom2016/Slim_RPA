namespace Tracto.Desktop.Recovery;

/// <summary>
/// Configuration for a bounded element wait operation.
/// </summary>
public sealed record WaitOptions
{
    /// <summary>The state predicate to poll for.</summary>
    public WaitCondition Condition { get; init; } = WaitCondition.Visible;

    /// <summary>Maximum milliseconds to wait before declaring a timeout.</summary>
    public int TimeoutMs { get; init; } = 5000;

    /// <summary>Milliseconds between successive state polls.</summary>
    public int PollIntervalMs { get; init; } = 250;
}
