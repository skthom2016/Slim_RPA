namespace Tracto.TerminalRuntime.Domain;

/// <summary>
/// Immutable snapshot of a single heartbeat transmission from the Terminal worker to the Orchestrator.
///
/// CEC v1.0.1 invariant #4: no plaintext secrets in any field.
/// CEC v1.0.1 invariant #3: <see cref="AttemptId"/> fences duplicate detection on the Orchestrator side.
/// </summary>
/// <param name="RunId">Logical run identifier.</param>
/// <param name="AttemptId">Current attempt identifier.  Fences duplicate-heartbeat detection.</param>
/// <param name="SeqNo">
///   Monotonically-increasing counter within the current attempt.
///   The Orchestrator uses this to detect stale / out-of-order deliveries.
/// </param>
/// <param name="SentAt">UTC wall-clock time this record was dispatched.</param>
/// <param name="ProgressSummary">
///   Bounded, sanitised progress description (max 512 characters).
///   MUST NOT contain secret values, tokens, credentials, or PII.
/// </param>
public sealed record HeartbeatRecord(
    string         RunId,
    string         AttemptId,
    long           SeqNo,
    DateTimeOffset SentAt,
    string         ProgressSummary);
