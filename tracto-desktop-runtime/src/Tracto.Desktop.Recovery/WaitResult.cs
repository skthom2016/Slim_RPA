namespace Tracto.Desktop.Recovery;

/// <summary>
/// Outcome of a completed <see cref="IElementWaiter.WaitAsync"/> call.
/// </summary>
public sealed record WaitResult
{
    /// <summary>True when the wait condition was satisfied before the timeout.</summary>
    public bool Resolved { get; init; }

    /// <summary>
    /// Human-readable description of the last observed element state before the wait resolved or timed out.
    /// Included in timeout diagnostics.
    /// </summary>
    public string? LastObservedState { get; init; }

    /// <summary>Approximate number of milliseconds elapsed during the wait (based on poll intervals).</summary>
    public long ElapsedMs { get; init; }
}
