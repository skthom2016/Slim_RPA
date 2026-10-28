namespace TractoRuntime.Browser.Cancellation;

/// <summary>Outcome of a <see cref="IRunCancellationCoordinator.CancelRunAsync"/> call.</summary>
public sealed record RunCancellationResult
{
    /// <summary>The run whose cancellation was requested.</summary>
    public required string RunId { get; init; }

    /// <summary>
    /// <see langword="true"/> when the run's <see cref="CancellationTokenSource"/> was found
    /// and cancelled successfully.
    /// <see langword="false"/> when the run was not registered (already completed or unknown).
    /// </summary>
    public required bool CancellationSignalled { get; init; }

    /// <summary>
    /// <see langword="true"/> when the run executor called
    /// <see cref="IRunCancellationCoordinator.SignalCleanupComplete"/> within the configured
    /// <see cref="RunCancellationOptions.CleanupTimeout"/> — meaning all resources were released
    /// cleanly by the run executor.
    /// <see langword="false"/> when the timeout expired and the coordinator performed a forced
    /// resource release instead.
    /// </summary>
    public required bool CleanupCompletedInTime { get; init; }

    /// <summary>Wall-clock time from cancellation signal to cleanup completion (normal or forced).</summary>
    public TimeSpan CleanupDuration { get; init; }
}
