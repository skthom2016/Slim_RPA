using System.Collections.Concurrent;
using Tracto.TerminalRuntime.Application;
using Tracto.TerminalRuntime.Domain;

namespace Tracto.TerminalRuntime.Infrastructure;

/// <summary>
/// Thread-safe PID tracker that maintains per-job process sets.
/// Enforces PID-tree isolation between Terminal workers sharing a host:
/// a PID registered under one jobId is never considered owned by another.
/// </summary>
public sealed class ProcessTracker : IProcessTracker
{
    private readonly ConcurrentDictionary<string, JobProcessSet> _sets = new();

    public void Track(string jobId, int pid)
    {
        var set = _sets.GetOrAdd(jobId, id => new JobProcessSet(id));
        set.Add(pid);
    }

    public void Untrack(string jobId, int pid)
    {
        if (_sets.TryGetValue(jobId, out var set))
            set.Remove(pid);
    }

    public bool IsOwnedByJob(string jobId, int pid)
    {
        if (!_sets.TryGetValue(jobId, out var set))
            return false;
        return set.IsOwned(pid);
    }

    public IReadOnlySet<int> GetPids(string jobId) =>
        _sets.TryGetValue(jobId, out var set)
            ? set.Pids
            : (IReadOnlySet<int>)new HashSet<int>();
}
