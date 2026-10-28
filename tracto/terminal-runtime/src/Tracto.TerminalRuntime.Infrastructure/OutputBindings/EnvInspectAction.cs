using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.OutputBindings;
using Tracto.TerminalRuntime.Domain.OutputBindings;

namespace Tracto.TerminalRuntime.Infrastructure.OutputBindings;

/// <summary>
/// Implements <c>terminal.env.inspect</c>.
///
/// Acceptance criteria (AC-5):
/// <list type="bullet">
///   <item>Name in allowlist → returns the current environment variable value as step output.</item>
///   <item>Name not in allowlist → <c>errorCategory=POLICY_DENIED</c>; no I/O performed.</item>
///   <item>Resolved value matching an active secretRef → automatically redacted;
///         <see cref="EnvInspectResult.WasRedacted"/> is set to true.</item>
/// </list>
///
/// This class never logs, writes to journal, or exposes secret values
/// (CEC invariant #4). The <c>activeSecretValues</c> parameter is a runtime-only
/// in-memory set and must not be persisted or forwarded by the caller.
/// </summary>
public sealed class EnvInspectAction : IEnvInspectAction
{
    private const string RedactionMarker = "[REDACTED]";

    /// <inheritdoc/>
    public Result<EnvInspectResult, Error> Inspect(
        string name,
        IReadOnlySet<string> allowlist,
        IReadOnlySet<string> activeSecretValues)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(allowlist);
        ArgumentNullException.ThrowIfNull(activeSecretValues);

        // AC-5: deny names outside the allowlist before any environment access.
        if (!allowlist.Contains(name))
        {
            return Result<EnvInspectResult, Error>.Fail(
                Error.PolicyDenied(
                    "terminal.env.inspect.notAllowlisted",
                    $"Environment variable '{name}' is not in the configured allowlist.",
                    "terminal.envDenied"));
        }

        // Read the environment variable; null is treated as empty string.
        var rawValue = System.Environment.GetEnvironmentVariable(name) ?? string.Empty;

        // AC-5: redact if the resolved value matches any active secret.
        var isSecret = activeSecretValues.Contains(rawValue);
        var safeValue = isSecret ? RedactionMarker : rawValue;

        return Result<EnvInspectResult, Error>.Ok(
            new EnvInspectResult(name, safeValue, isSecret));
    }
}
