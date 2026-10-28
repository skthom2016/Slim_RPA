namespace Tracto.Desktop.Recovery;

/// <summary>
/// Thrown to propagate an UNKNOWN run/step outcome with its <c>unknownReason</c> through the call stack.
/// <para>
/// The <see cref="UnknownReason"/> property maps directly to the CEC v1.0.1
/// <c>unknownReason</c> field emitted in the final result envelope.
/// </para>
/// </summary>
public sealed class UnknownOutcomeException : Exception
{
    /// <summary>Gets the CEC v1.0.1 <c>unknownReason</c> code associated with this uncertain outcome.</summary>
    public string UnknownReason { get; }

    /// <summary>
    /// Initialises the exception with the CEC v1.0.1 <c>unknownReason</c> code.
    /// </summary>
    /// <param name="unknownReason">The reason code for the unknown outcome (e.g. <c>action.commitUncertain</c>).</param>
    public UnknownOutcomeException(string unknownReason)
        : base($"Execution outcome is UNKNOWN. Reason: {unknownReason}")
    {
        UnknownReason = unknownReason;
    }
}
