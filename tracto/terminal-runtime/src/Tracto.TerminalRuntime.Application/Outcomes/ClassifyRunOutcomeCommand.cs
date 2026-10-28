using Tracto.TerminalRuntime.Domain;

namespace Tracto.TerminalRuntime.Application.Outcomes;

/// <summary>
/// Describes the raw evidence gathered at the end of a process execution attempt.
/// Passed to <see cref="IRunOutcomeClassifier.ClassifyAsync"/> to determine the
/// canonical <c>RunOutcome</c> for the attempt.
///
/// CEC v1.0.1 invariant #4: <see cref="StdoutContent"/> must have already been
/// redacted by <c>IOutputRedactor</c> before the command is constructed — no
/// plaintext secrets may appear in any field here.
/// </summary>
/// <param name="RunId">Unique identifier of the logical run.</param>
/// <param name="AttemptId">Identifier of this specific execution attempt.</param>
/// <param name="IdempotencyClass">
///   The idempotency classification of the command that was executed.
///   Irreversible commands that produced write evidence but no exit receipt are
///   classified as UNKNOWN with no-retry (CEC invariants #2 and #3).
/// </param>
/// <param name="StdoutContent">
///   Redacted stdout produced by the process. Used to detect write-activity evidence
///   when no exit code was received (AC-2).
///   Must NOT contain plaintext secrets (CEC invariant #4).
/// </param>
/// <param name="ExitCodeReceived">
///   True when the process exited cleanly and the runtime captured the exit code.
///   False when the process was killed, the transport was lost, or a timeout
///   interrupted receipt of the exit code.
/// </param>
/// <param name="ExitCode">
///   The process exit code. Null when <see cref="ExitCodeReceived"/> is <c>false</c>.
/// </param>
/// <param name="FailureMode">
///   Indicates how the execution attempt ended abnormally.
///   <see cref="ExecutionFailureMode.None"/> when the process exited normally.
/// </param>
/// <param name="AttemptCount">
///   The number of attempts made so far (1-based; 1 on first attempt).
///   Used together with <see cref="MaxRetries"/> to decide whether AC-1 (read-only
///   transient retry) is still allowed.
/// </param>
/// <param name="MaxRetries">
///   Maximum number of retry attempts configured for this run.
///   0 means no retries are allowed regardless of idempotency class.
/// </param>
public sealed record ClassifyRunOutcomeCommand(
    string           RunId,
    string           AttemptId,
    IdempotencyClass IdempotencyClass,
    string           StdoutContent,
    bool             ExitCodeReceived,
    int?             ExitCode,
    ExecutionFailureMode FailureMode,
    int              AttemptCount,
    int              MaxRetries);

/// <summary>
/// Describes how a process execution attempt ended abnormally.
/// </summary>
public enum ExecutionFailureMode
{
    /// <summary>The process exited with an exit code (may still be a non-zero failure).</summary>
    None,

    /// <summary>
    /// The transport channel (pipe, SSH, named-pipe) was closed or broken before the
    /// exit code was received. Maps to <c>UnknownReason.TransportLostAfterWrite</c>
    /// when write evidence is present on an irreversible command.
    /// </summary>
    TransportLost,

    /// <summary>
    /// The OS terminated the process (SIGKILL, TerminateProcess, job-object kill)
    /// before a clean exit. Maps to <c>UnknownReason.ProcessKilledAfterWrite</c>
    /// when write evidence is present on an irreversible command.
    /// </summary>
    ProcessKilled,

    /// <summary>
    /// The run exceeded its configured time limit before the exit code was received.
    /// Maps to <c>UnknownReason.TimeoutDuringIrreversibleWrite</c> when write
    /// evidence is present on an irreversible command.
    /// </summary>
    Timeout,

    /// <summary>
    /// A transient, retriable error (network blip, temporary resource unavailability)
    /// that did not produce observable side-effects. Eligible for retry on read-only
    /// and idempotent commands (AC-1).
    /// </summary>
    TransientError
}
