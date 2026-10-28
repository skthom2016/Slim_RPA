using Microsoft.Extensions.Logging;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.Outcomes;
using Tracto.TerminalRuntime.Domain;
using Tracto.TerminalRuntime.Domain.Outcomes;

namespace Tracto.TerminalRuntime.Infrastructure.Outcomes;

/// <summary>
/// Infrastructure implementation of <see cref="IRunOutcomeClassifier"/>.
///
/// Classification rules (CEC v1.0.1, TER-US-016):
///
///   1. <b>Irreversible + write evidence + no exit code</b>
///      → <c>UNKNOWN</c>, <c>needsAttention=true</c>, no retry.
///      The unknownReason is set from the <see cref="ExecutionFailureMode"/>:
///        - TransportLost  → <see cref="UnknownReason.TransportLostAfterWrite"/>
///        - ProcessKilled  → <see cref="UnknownReason.ProcessKilledAfterWrite"/>
///        - Timeout        → <see cref="UnknownReason.TimeoutDuringIrreversibleWrite"/>
///        - Any other      → <see cref="UnknownReason.InternalErrorAfterWrite"/>
///
///   2. <b>Read-only or Idempotent + transient error + attempt count below MaxRetries</b>
///      → <c>ShouldRetry=true</c> (AC-1).
///
///   3. <b>Clean exit (exit code received)</b>
///      → <c>SUCCEEDED</c> on exit code 0, <c>FAILED</c> on non-zero.
///
///   4. <b>All other non-retriable failures</b>
///      → <c>FAILED</c>, no retry.
///
/// Stdout write-detection is purely pattern-based against the already-redacted
/// <see cref="ClassifyRunOutcomeCommand.StdoutContent"/>. No new process or I/O
/// is initiated here (CEC invariant #5).
/// </summary>
public sealed class RunOutcomeClassifier(ILogger<RunOutcomeClassifier> logger) : IRunOutcomeClassifier
{
    // Keywords that strongly imply a write operation reached the remote system.
    // These are matched case-insensitively against the redacted stdout.
    private static readonly string[] WriteIndicators =
    [
        "written",
        "created",
        "updated",
        "deleted",
        "inserted",
        "committed",
        "applied",
        "deployed",
        "uploaded",
        "saved",
        "modified",
        "transferred",
        "sent",
        "submitted",
        "processed",
        // Common structured success markers
        "\"status\":\"success\"",
        "\"result\":\"ok\"",
        "rows affected",
        "record(s) affected"
    ];

    public Task<Result<RunOutcome, Error>> ClassifyAsync(
        ClassifyRunOutcomeCommand command,
        CancellationToken         ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // ── Rule 1: Irreversible + write evidence + no exit code → UNKNOWN ────
        if (command.IdempotencyClass == IdempotencyClass.Irreversible
            && !command.ExitCodeReceived
            && StdoutIndicatesWrite(command.StdoutContent))
        {
            var reason   = MapFailureModeToUnknownReason(command.FailureMode);
            var category = MapFailureModeToErrorCategory(command.FailureMode);

            logger.LogWarning(
                "Run {RunId} attempt {AttemptId}: irreversible command produced write evidence but " +
                "exit code was not received (failureMode={FailureMode}). " +
                "Classifying as UNKNOWN with needsAttention=true. No auto-retry.",
                command.RunId, command.AttemptId, command.FailureMode);

            return Task.FromResult(
                Result<RunOutcome, Error>.Ok(RunOutcome.UnknownNoRetry(reason, category)));
        }

        // ── Rule 2: Read-only or idempotent + transient error + attempts below limit → retry ─
        // Idempotent commands (e.g. upsert, PUT) are safely retriable on transient failures
        // because re-executing them produces the same net result (CEC v1.0.1, TER-US-016 AC-1).
        if ((command.IdempotencyClass == IdempotencyClass.ReadOnly
             || command.IdempotencyClass == IdempotencyClass.Idempotent)
            && command.FailureMode   == ExecutionFailureMode.TransientError
            && command.AttemptCount  <  command.MaxRetries)
        {
            logger.LogInformation(
                "Run {RunId} attempt {AttemptId}: {IdempotencyClass} transient failure; " +
                "attempt {Attempt}/{Max} — scheduling retry.",
                command.RunId, command.AttemptId, command.IdempotencyClass,
                command.AttemptCount, command.MaxRetries);

            return Task.FromResult(
                Result<RunOutcome, Error>.Ok(RunOutcome.RetriableFailure(ErrorCategory.Transport)));
        }

        // ── Rule 3: Exit code received → success or failure ───────────────────
        if (command.ExitCodeReceived)
        {
            if (command.ExitCode == 0)
            {
                return Task.FromResult(
                    Result<RunOutcome, Error>.Ok(RunOutcome.Succeeded()));
            }

            logger.LogInformation(
                "Run {RunId} attempt {AttemptId}: process exited with non-zero code {ExitCode}.",
                command.RunId, command.AttemptId, command.ExitCode);

            return Task.FromResult(
                Result<RunOutcome, Error>.Ok(RunOutcome.Failed(ErrorCategory.Internal)));
        }

        // ── Rule 4: All other non-retriable failures ──────────────────────────
        // Covers: Irreversible without write evidence (safe to mark FAILED, not UNKNOWN),
        // Idempotent commands that exhausted retries, etc.
        logger.LogWarning(
            "Run {RunId} attempt {AttemptId}: unclassified failure (idempotencyClass={Class}, " +
            "failureMode={FailureMode}, exitCodeReceived={ExitCodeReceived}). Classifying as FAILED.",
            command.RunId, command.AttemptId, command.IdempotencyClass,
            command.FailureMode, command.ExitCodeReceived);

        return Task.FromResult(
            Result<RunOutcome, Error>.Ok(RunOutcome.Failed(ErrorCategory.Internal)));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when the already-redacted stdout content contains at least one
    /// pattern that strongly implies a write operation reached the remote system.
    ///
    /// This is a best-effort heuristic. A false-negative (missed write) causes the
    /// irreversible command to be classified FAILED rather than UNKNOWN, which is
    /// safer than a false-positive because the operator still sees the non-zero exit
    /// or transport error. A false-positive (false write indicator on a read-only
    /// path) cannot occur here because this method is only invoked for Irreversible
    /// commands.
    /// </summary>
    internal static bool StdoutIndicatesWrite(string? stdoutContent)
    {
        if (string.IsNullOrWhiteSpace(stdoutContent))
            return false;

        foreach (var indicator in WriteIndicators)
        {
            if (stdoutContent.Contains(indicator, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static UnknownReason MapFailureModeToUnknownReason(ExecutionFailureMode mode) =>
        mode switch
        {
            ExecutionFailureMode.TransportLost  => UnknownReason.TransportLostAfterWrite,
            ExecutionFailureMode.ProcessKilled  => UnknownReason.ProcessKilledAfterWrite,
            ExecutionFailureMode.Timeout        => UnknownReason.TimeoutDuringIrreversibleWrite,
            _                                   => UnknownReason.InternalErrorAfterWrite
        };

    private static ErrorCategory MapFailureModeToErrorCategory(ExecutionFailureMode mode) =>
        mode switch
        {
            ExecutionFailureMode.TransportLost  => ErrorCategory.Transport,
            ExecutionFailureMode.Timeout        => ErrorCategory.Timeout,
            _                                   => ErrorCategory.Internal
        };
}
