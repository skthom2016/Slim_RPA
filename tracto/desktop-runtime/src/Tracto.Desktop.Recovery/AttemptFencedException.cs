namespace Tracto.Desktop.Recovery;

/// <summary>
/// Thrown when an action is attempted after the execution attempt has been fenced.
/// <para>
/// Per DT-16-AC06: once an attempt is fenced (UNKNOWN with <c>needsAttention=true</c>),
/// no subsequent automation step — including read-only queries — may execute.
/// Continuation requires a new valid <c>attemptId</c> issued by the Orchestrator after reconciliation.
/// </para>
/// </summary>
public sealed class AttemptFencedException : Exception
{
    /// <summary>Gets the attempt identifier that was already fenced when this exception was thrown.</summary>
    public string AttemptId { get; }

    /// <summary>
    /// Initialises the exception with the fenced attempt identifier.
    /// </summary>
    /// <param name="attemptId">The attempt ID that is already fenced.</param>
    public AttemptFencedException(string attemptId)
        : base($"Execution attempt '{attemptId}' has been fenced. No further actions may execute until a new attempt is authorised by the Orchestrator.")
    {
        AttemptId = attemptId;
    }
}
