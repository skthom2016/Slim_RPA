namespace Tracto.TerminalRuntime.Domain;

/// <summary>
/// Classifies a command step by its replay safety.
/// Used during lease-expiry reconciliation to decide whether the step can be
/// retried after an Orchestrator restart (TER-US-010, CEC v1.0.1 invariant #3).
/// </summary>
public enum IdempotencyClass
{
    /// <summary>
    /// Executing the step produces no observable side-effects and may be retried
    /// freely on any transient failure (e.g. a read-only query or file listing).
    /// </summary>
    ReadOnly,

    /// <summary>Executing the step more than once produces the same observable result.</summary>
    Idempotent,

    /// <summary>
    /// Executing the step more than once has side-effects that cannot be undone
    /// (e.g. sends an email, submits a payment, deploys a binary).
    /// Such steps must NEVER be auto-replayed after a lease expiry or ambiguous timeout.
    /// </summary>
    Irreversible
}
