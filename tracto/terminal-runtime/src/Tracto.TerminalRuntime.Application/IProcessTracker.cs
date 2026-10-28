namespace Tracto.TerminalRuntime.Application;

/// <summary>
/// Tracks OS process IDs per job to enforce PID-tree isolation between
/// two Terminal workers that share a host.
/// </summary>
public interface IProcessTracker
{
    /// <summary>Register a PID as belonging to the given job.</summary>
    void Track(string jobId, int pid);

    /// <summary>Remove a PID from the given job's tracked set.</summary>
    void Untrack(string jobId, int pid);

    /// <summary>
    /// Returns true when the PID is tracked by <paramref name="jobId"/>.
    /// Returns false when the PID belongs to a different job or is unknown.
    /// </summary>
    bool IsOwnedByJob(string jobId, int pid);

    /// <summary>Returns all PIDs currently tracked for a job.</summary>
    IReadOnlySet<int> GetPids(string jobId);
}
