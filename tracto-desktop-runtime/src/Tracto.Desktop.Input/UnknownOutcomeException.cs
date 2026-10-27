namespace Tracto.Desktop.Input;

/// <summary>
/// Thrown when an irreversible action completed but its outcome cannot be verified.
/// Maps to CEC v1.0.1 step state UNKNOWN with the supplied unknownReason.
/// </summary>
public sealed class UnknownOutcomeException : Exception
{
    public string UnknownReason { get; }

    public UnknownOutcomeException(string unknownReason)
        : base($"Action outcome is uncertain: {unknownReason}")
    {
        UnknownReason = unknownReason;
    }

    public UnknownOutcomeException(string unknownReason, Exception inner)
        : base($"Action outcome is uncertain: {unknownReason}", inner)
    {
        UnknownReason = unknownReason;
    }
}
