namespace Tracto.Desktop.Recovery;

/// <summary>
/// Result of a session-lock monitoring run.
/// </summary>
/// <param name="Expired">True when the lock persisted beyond the configured policy limit.</param>
/// <param name="Resumed">True when the session unlocked before the policy limit was reached.</param>
/// <param name="ErrorCategory">CEC v1.0.1 errorCategory string when <see cref="Expired"/> is true; empty otherwise.</param>
public sealed record LockMonitorResult(bool Expired, bool Resumed, string ErrorCategory);
