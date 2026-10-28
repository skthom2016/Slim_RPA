using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Outcomes;

namespace Tracto.TerminalRuntime.Application.Outcomes;

/// <summary>
/// Classifies the outcome of a run execution attempt into a <see cref="RunOutcome"/>
/// based on the command's idempotency class, the evidence on stdout, and whether
/// the process exit code was received.
///
/// CEC v1.0.1 rules enforced:
///   - Irreversible command + write evidence + no exit code → UNKNOWN, needsAttention=true, no retry.
///   - Read-only command + transient error + attempt below limit → retriable failure.
///   - UNKNOWN outcome is never auto-replayed (caller must honour <see cref="RunOutcome.ShouldRetry"/>).
///
/// No process-launch or I/O logic lives here (CEC invariant #5).
/// </summary>
public interface IRunOutcomeClassifier
{
    /// <summary>
    /// Classifies the given raw evidence into a <see cref="RunOutcome"/>.
    /// </summary>
    /// <param name="command">The evidence gathered at the end of the execution attempt.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///   A successful <see cref="Result{T,Error}"/> wrapping the classified <see cref="RunOutcome"/>,
    ///   or a failure when the classifier itself encounters an internal error.
    /// </returns>
    Task<Result<RunOutcome, Error>> ClassifyAsync(
        ClassifyRunOutcomeCommand command,
        CancellationToken         ct = default);
}
