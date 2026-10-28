namespace TractoRuntime.Browser.Cancellation;

/// <summary>
/// Configuration for <see cref="RunCancellationCoordinator"/> (BRW-US-016 AC#2).
/// </summary>
public sealed record RunCancellationOptions
{
    /// <summary>
    /// Maximum time to wait for in-flight operations (downloads, navigations, action steps)
    /// to stop cleanly after the cancellation signal is sent.
    /// When this timeout expires the coordinator performs a forced resource release.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan CleanupTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
