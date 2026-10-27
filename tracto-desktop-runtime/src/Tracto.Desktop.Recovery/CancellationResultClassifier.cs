namespace Tracto.Desktop.Recovery;

/// <summary>
/// Determines the correct <see cref="CancellationOutcome"/> based on whether any
/// input was dispatched before the cancellation signal arrived (DT-17-AC05).
/// </summary>
public static class CancellationResultClassifier
{
    /// <summary>
    /// Returns <see cref="CancellationOutcome.Cancelled"/> when no input was sent,
    /// or <see cref="CancellationOutcome.Unknown"/> (with reason
    /// <c>"action.commitUncertain"</c>) when input was sent but the postcondition
    /// was not confirmed.
    /// </summary>
    /// <param name="anyInputSent">
    ///   <c>true</c> when at least one mouse, keyboard or clipboard input event was
    ///   delivered to the OS before cancellation; <c>false</c> if the action was
    ///   aborted before any side effect.
    /// </param>
    /// <returns>The appropriate <see cref="CancellationOutcome"/> value.</returns>
    public static CancellationOutcome Classify(bool anyInputSent)
    {
        if (!anyInputSent)
            return CancellationOutcome.Cancelled;

        // Input was sent but postcondition was never confirmed.
        // Callers must report unknownReason = "action.commitUncertain".
        return CancellationOutcome.Unknown;
    }

    /// <summary>
    /// The <c>unknownReason</c> value to include in the Orchestrator result message
    /// whenever <see cref="Classify"/> returns <see cref="CancellationOutcome.Unknown"/>.
    /// </summary>
    public const string UnknownReason = "action.commitUncertain";
}
