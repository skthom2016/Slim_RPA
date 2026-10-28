using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.OutputBindings;

namespace Tracto.TerminalRuntime.Application.OutputBindings;

/// <summary>
/// Implements the <c>terminal.env.inspect</c> built-in action.
/// Only allowlisted environment variable names are permitted (AC-5).
/// Resolved values that match an active secretRef are automatically redacted
/// from step output and journal evidence (AC-5, CEC invariant #4).
/// </summary>
public interface IEnvInspectAction
{
    /// <summary>
    /// Inspects the environment variable identified by <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The environment variable name requested by the step.</param>
    /// <param name="allowlist">The set of allowlisted environment variable names.</param>
    /// <param name="activeSecretValues">
    ///   The set of currently-active secret values (resolved from secretRefs).
    ///   If the resolved env value is found in this set it is redacted automatically.
    ///   Callers must not log or persist this parameter.
    /// </param>
    /// <returns>
    ///   <see cref="EnvInspectResult"/> on success (value may be a redaction marker),
    ///   or a failure with <c>errorCategory=POLICY_DENIED</c> when <paramref name="name"/>
    ///   is not in the allowlist.
    /// </returns>
    Result<EnvInspectResult, Error> Inspect(
        string name,
        IReadOnlySet<string> allowlist,
        IReadOnlySet<string> activeSecretValues);
}
