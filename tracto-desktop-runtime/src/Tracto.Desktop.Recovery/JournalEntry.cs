namespace Tracto.Desktop.Recovery;

/// <summary>
/// An immutable journal record written whenever a step produces an UNKNOWN outcome or another
/// durable event (crash, lock, focus theft) that must survive a Runtime restart for reconciliation.
/// </summary>
/// <param name="AttemptId">The CEC v1.0.1 attempt identifier for the current execution attempt.</param>
/// <param name="StepId">The identifier of the step that produced this entry.</param>
/// <param name="LastAction">A sanitised description of the last action that was dispatched before the uncertain outcome.</param>
/// <param name="UnknownReason">
/// The CEC v1.0.1 <c>unknownReason</c> code (e.g. <c>action.commitUncertain</c>) or an empty
/// string for non-UNKNOWN events such as crash or lock notifications.
/// </param>
/// <param name="Timestamp">Unix epoch milliseconds at the time the entry was written.</param>
public sealed record JournalEntry(
    string AttemptId,
    string StepId,
    string LastAction,
    string UnknownReason,
    long Timestamp);
