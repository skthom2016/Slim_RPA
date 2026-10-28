using Tracto.Desktop.Session;

namespace Tracto.Desktop.Recovery;

/// <summary>
/// Monitors a locked Windows desktop session and sends continuous heartbeats while the lock persists.
/// </summary>
/// <remarks>
/// Per DT-16 spec:
/// <list type="bullet">
///   <item>The run state remains RUNNING throughout the lock period — no PAUSED state is emitted.</item>
///   <item>Heartbeats are sent at every <see cref="HeartbeatIntervalMs"/> interval while the session is locked.</item>
///   <item>If the lock is released within the policy limit, <see cref="LockMonitorResult.Resumed"/> is true.</item>
///   <item>If the lock persists past the configured policy limit, <see cref="LockMonitorResult.Expired"/> is true
///       and <see cref="LockMonitorResult.ErrorCategory"/> is <c>"TIMEOUT"</c>.</item>
/// </list>
/// </remarks>
public sealed class SessionLockMonitor
{
    /// <summary>
    /// Interval between successive heartbeat sends and session-state polls, in milliseconds.
    /// </summary>
    public const int HeartbeatIntervalMs = 1_000;

    /// <summary>
    /// Monitors the session state, sending heartbeats while locked.
    /// Returns once the session is unlocked or the policy limit is exceeded.
    /// </summary>
    /// <param name="provider">Provides the current session state.</param>
    /// <param name="hb">Sends heartbeats to keep the job lease alive.</param>
    /// <param name="policyLimit">Maximum time the lock may persist before the job fails.</param>
    /// <param name="timer">Timer abstraction (use <see cref="FakeTimer"/> in tests).</param>
    /// <param name="leaseId">The active job lease identifier passed to every heartbeat.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<LockMonitorResult> MonitorAsync(
        ISessionStateProvider provider,
        IHeartbeatSender hb,
        TimeSpan policyLimit,
        ITimer timer,
        string leaseId,
        CancellationToken ct)
    {
        long elapsedMs = 0;
        long limitMs = (long)policyLimit.TotalMilliseconds;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var state = provider.GetCurrentState();

            // Session is no longer locked — return as resumed.
            if (state != SessionState.Locked && state != SessionState.Disconnected)
                return new LockMonitorResult(Expired: false, Resumed: true, ErrorCategory: string.Empty);

            // Policy limit exceeded — the Orchestrator will classify this as TIMED_OUT / FAILED.
            if (elapsedMs >= limitMs)
                return new LockMonitorResult(Expired: true, Resumed: false, ErrorCategory: "TIMEOUT");

            // Keep the lease alive while waiting for the unlock.
            await hb.SendHeartbeatAsync(leaseId, ct).ConfigureAwait(false);

            // Wait one heartbeat interval then re-check.
            await timer.DelayAsync(HeartbeatIntervalMs, ct).ConfigureAwait(false);
            elapsedMs += HeartbeatIntervalMs;
        }
    }
}
