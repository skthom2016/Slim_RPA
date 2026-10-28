namespace Tracto.TerminalRuntime.Domain;

/// <summary>
/// Governs the frequency and failure-tolerance of outbound heartbeats sent by the Terminal
/// worker while a long-running command executes (TER-US-018).
/// </summary>
/// <param name="IntervalMs">
///   Milliseconds between successive heartbeat attempts.  Default: 15 000 ms (15 s).
///   A value of zero or less is treated as the default by callers that validate the policy.
/// </param>
/// <param name="MaxFailures">
///   Maximum consecutive heartbeat failures tolerated before the worker treats the lease
///   as effectively expired.  Termination happens only when failures have reached this
///   threshold AND the lease has actually expired (or the absolute deadline has passed).
///   Default: 5.
/// </param>
public sealed record HeartbeatPolicy(
    int IntervalMs  = 15_000,
    int MaxFailures = 5);
