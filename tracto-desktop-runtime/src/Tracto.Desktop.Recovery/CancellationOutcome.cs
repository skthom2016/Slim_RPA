namespace Tracto.Desktop.Recovery;

/// <summary>
/// Classifies the outcome of a job cancellation with respect to input side effects (DT-17-AC05).
/// </summary>
public enum CancellationOutcome
{
    /// <summary>
    /// No input was sent before cancellation arrived; the action had no side effect.
    /// The run result should be reported as CANCELLED.
    /// </summary>
    Cancelled,

    /// <summary>
    /// Input was sent but the postcondition was not confirmed; the outcome is uncertain.
    /// The run result should be reported as UNKNOWN with
    /// <c>unknownReason = "action.commitUncertain"</c>.
    /// </summary>
    Unknown
}
