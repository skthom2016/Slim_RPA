namespace Tracto.TerminalRuntime.Domain.Process;

/// <summary>
/// Immutable snapshot of the OS process tree owned by a single terminal run.
/// Carries the root PID, the set of all child PIDs discovered at launch time,
/// the wall-clock start instant, and the declared timeout budget.
///
/// CEC v1.0.1: the runtime must terminate the entire tree when the timeout
/// elapses and must verify that no owned PID survives the cleanup grace period.
/// </summary>
/// <param name="RunId">The run that owns this process tree.</param>
/// <param name="RootPid">OS PID of the top-level shell process.</param>
/// <param name="ChildPids">
///   Mutable set of descendant PIDs accumulated during execution.
///   Callers may add PIDs discovered after launch; equality is by reference.
/// </param>
/// <param name="StartedAt">UTC wall-clock instant the root process was started.</param>
/// <param name="TimeoutMs">
///   Maximum execution time in milliseconds before the runtime must terminate
///   the tree and transition the run to TIMED_OUT.
///   Zero or negative means no deadline (use with care).
/// </param>
public sealed record OwnedProcessTree(
    string RunId,
    int RootPid,
    HashSet<int> ChildPids,
    DateTimeOffset StartedAt,
    int TimeoutMs)
{
    /// <summary>
    /// Returns the deadline instant, or <see langword="null"/> when
    /// <see cref="TimeoutMs"/> is zero or negative (no deadline).
    /// </summary>
    public DateTimeOffset? Deadline =>
        TimeoutMs > 0 ? StartedAt.AddMilliseconds(TimeoutMs) : null;

    /// <summary>
    /// All PIDs owned by this tree (root + children).
    /// The root PID is always a member, even when callers construct the tree with
    /// an incomplete or empty <see cref="ChildPids"/> set (e.g. during deserialization).
    /// </summary>
    public IReadOnlySet<int> AllPids =>
        ChildPids.Contains(RootPid)
            ? ChildPids
            : new HashSet<int>(ChildPids) { RootPid };
}
