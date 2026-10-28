using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Arguments;
using Tracto.TerminalRuntime.Domain.Redaction;

namespace Tracto.TerminalRuntime.Application.Execution;

/// <summary>
/// Launches a terminal process from a validated <see cref="ProcessStartSpec"/>,
/// captures its stdout and stderr, applies output redaction via <see cref="IOutputRedactor"/>
/// before any storage or forwarding, and returns the sanitised result.
///
/// CEC invariant #4: no plaintext secret values may appear in any downstream artifact,
/// journal entry, or Orchestrator payload. All captured output MUST pass through this
/// runner (or equivalent) so that redaction is applied on the hot path.
/// </summary>
public interface ITerminalProcessRunner
{
    /// <summary>
    /// Runs the process described by <paramref name="spec"/>, captures its output,
    /// and returns redacted stdout and stderr content together with the exit code.
    /// </summary>
    /// <param name="spec">A fully-validated process start specification.</param>
    /// <param name="policy">
    ///   The active redaction policy. Must be constructed with any resolved secret token
    ///   values before calling. Use <see cref="OutputRedactionPolicy.Default"/> (MaskAll)
    ///   when no explicit secretRef is present.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///   <see cref="Result{T,Error}"/> wrapping a <see cref="Execution.ProcessRunResult"/> on success,
    ///   or an <see cref="Error"/> when the policy is invalid or the process cannot be started.
    /// </returns>
    Task<Result<ProcessRunResult, Error>> RunAsync(
        ProcessStartSpec       spec,
        OutputRedactionPolicy  policy,
        CancellationToken      ct = default);
}
