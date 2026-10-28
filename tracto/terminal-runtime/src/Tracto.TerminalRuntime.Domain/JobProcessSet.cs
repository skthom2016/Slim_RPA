using System.Collections.Concurrent;

namespace Tracto.TerminalRuntime.Domain;

/// <summary>
/// Tracks the set of OS process IDs (PIDs) owned by a single job.
/// Provides isolation boundary between two Terminal workers sharing a host.
/// Thread-safe: concurrent Add/Remove/Contains from multiple threads is safe.
/// </summary>
public sealed class JobProcessSet
{
    // ConcurrentDictionary<int, byte> used as a concurrent hash-set.
    // byte value is unused; TryAdd/TryRemove/ContainsKey are all lock-free.
    private readonly ConcurrentDictionary<int, byte> _pids = new();

    public string JobId { get; }

    public JobProcessSet(string jobId) => JobId = jobId;

    /// <summary>Returns a point-in-time snapshot of the PIDs; safe to enumerate without a lock.</summary>
    public IReadOnlySet<int> Pids => _pids.Keys.ToHashSet();

    public void Add(int pid) => _pids.TryAdd(pid, 0);

    public void Remove(int pid) => _pids.TryRemove(pid, out _);

    public bool Contains(int pid) => _pids.ContainsKey(pid);

    /// <summary>Returns true when the PID belongs to THIS job and not to any other tracked job.</summary>
    public bool IsOwned(int pid) => _pids.ContainsKey(pid);
}
