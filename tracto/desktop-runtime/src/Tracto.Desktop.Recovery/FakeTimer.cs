namespace Tracto.Desktop.Recovery;

/// <summary>
/// Controllable test double for <see cref="ITimer"/>.
/// <para>
/// Call <see cref="AdvanceMs"/> to make time pass in the fake clock.
/// Any <see cref="DelayAsync"/> call whose requested duration has been exceeded by the
/// total advanced time completes immediately (or synchronously inline when already past).
/// </para>
/// </summary>
public sealed class FakeTimer : ITimer
{
    private long _totalElapsed;
    private readonly List<(long TargetMs, TaskCompletionSource<bool> Tcs)> _pending = new();
    private readonly object _lock = new();

    /// <summary>
    /// Advances the fake clock by <paramref name="ms"/> milliseconds.
    /// Completes all pending delays whose target time is now reachable.
    /// </summary>
    public void AdvanceMs(int ms)
    {
        if (ms < 0)
            throw new ArgumentOutOfRangeException(nameof(ms));

        List<TaskCompletionSource<bool>> toComplete = new();

        lock (_lock)
        {
            _totalElapsed += ms;

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var (target, tcs) = _pending[i];
                if (_totalElapsed >= target)
                {
                    toComplete.Add(tcs);
                    _pending.RemoveAt(i);
                }
            }
        }

        // Complete outside the lock to avoid deadlocks; continuations may call back into FakeTimer.
        foreach (var tcs in toComplete)
            tcs.TrySetResult(true);
    }

    /// <inheritdoc />
    public Task DelayAsync(int ms, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (ms <= 0)
            return Task.CompletedTask;

        TaskCompletionSource<bool> tcs;

        lock (_lock)
        {
            long target = _totalElapsed + ms;

            if (_totalElapsed >= target)
                return Task.CompletedTask;

            tcs = new TaskCompletionSource<bool>();
            _pending.Add((target, tcs));
        }

        // Wire up cancellation without holding the lock.
        ct.Register(() => tcs.TrySetCanceled(), useSynchronizationContext: false);

        return tcs.Task;
    }
}
