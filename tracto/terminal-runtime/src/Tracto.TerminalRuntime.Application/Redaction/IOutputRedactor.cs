using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Redaction;

namespace Tracto.TerminalRuntime.Application.Redaction;

/// <summary>
/// Sanitises captured command output before it is stored in journals,
/// forwarded to the Orchestrator, or included in evidence artifacts.
/// </summary>
public interface IOutputRedactor
{
    /// <summary>
    /// Redacts <paramref name="rawOutput"/> according to <paramref name="policy"/>.
    /// </summary>
    /// <param name="rawOutput">The unfiltered stdout or stderr text.</param>
    /// <param name="policy">
    ///   The active redaction policy.  Must contain all resolved secret token values
    ///   that the caller obtained from <see cref="Interfaces.ISecretStore"/>.
    /// </param>
    /// <returns>
    ///   <see cref="Result{T,Error}"/> wrapping a <see cref="RedactedOutput"/> on success,
    ///   or an <see cref="Error"/> when the policy is invalid (e.g. mode is disabled).
    /// </returns>
    Result<RedactedOutput, Error> Redact(string rawOutput, OutputRedactionPolicy policy);

    /// <summary>
    /// Validates that <paramref name="policy"/> is legally configured.
    /// Specifically, a policy with no redaction mode at all is rejected.
    /// </summary>
    Result<bool, Error> ValidatePolicy(OutputRedactionPolicy policy);
}
