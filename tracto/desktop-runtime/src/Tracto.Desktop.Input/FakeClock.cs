namespace Tracto.Desktop.Input;

/// <summary>
/// Test double for IClock. Pending delays complete only when AdvanceMs is called.
/// </summary>
public sealed class FakeClock : IClock
{
    private long _currentMs;
    private readonly List<PendingDelay> _pending = new();
    private readonly object _lock = new();

    public long CurrentMs => Volatile.Read(ref _currentMs);

    /// <summary>Advances the fake clock by <paramref name="ms"/> milliseconds, completing any delays that are now due.</summary>
    public void AdvanceMs(long ms)
    {
        List<PendingDelay> due;
        lock (_lock)
        {
            _currentMs += ms;
            due = _pending.Where(p => p.DueAtMs <= _currentMs).ToList();
            foreach (var d in due) _pending.Remove(d);
        }
        foreach (var d in due) d.Complete();
    }

    public Task DelayAsync(int ms, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        ct.Register(() => tcs.TrySetCanceled(ct));

        long dueAt;
        lock (_lock)
        {
            dueAt = _currentMs + ms;
            if (dueAt <= _currentMs)
            {
                tcs.TrySetResult();
                return tcs.Task;
            }
            _pending.Add(new PendingDelay(dueAt, tcs));
        }
        return tcs.Task;
    }

    private sealed record PendingDelay(long DueAtMs, TaskCompletionSource Tcs)
    {
        public void Complete() => Tcs.TrySetResult();
    }
}
