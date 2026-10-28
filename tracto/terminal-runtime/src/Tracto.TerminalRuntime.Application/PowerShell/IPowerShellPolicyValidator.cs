using Tracto.TerminalRuntime.Application.Common;

namespace Tracto.TerminalRuntime.Application.PowerShell;

/// <summary>
/// Validates a PowerShell command invocation against deployment policy before any process launches.
/// Implementations live in Infrastructure; this interface keeps Application free of process logic.
/// </summary>
public interface IPowerShellPolicyValidator
{
    /// <summary>
    /// Validates the <paramref name="command"/> against the embedded <see cref="Domain.Policies.PowerShellPolicy"/>.
    /// Returns <see cref="PowerShellValidationResult"/> on success, or a typed
    /// <see cref="Error"/> (VALIDATION / POLICY_DENIED) on rejection.
    /// No process is launched by this call.
    /// </summary>
    Task<Result<PowerShellValidationResult, Error>> ValidateAsync(
        ValidatePowerShellCommandCommand command,
        CancellationToken ct = default);
}

/// <summary>
/// Value returned when policy validation passes.
/// Carries the final, safe pwsh.exe argument list ready for process launch.
/// </summary>
/// <param name="SafeArguments">
///   The argument list to pass to pwsh.exe, including policy-mandated flags
///   (-NonInteractive -NoProfile -ExecutionPolicy &lt;policy&gt;).
/// </param>
/// <param name="EffectiveLanguageMode">
///   The language mode that will be enforced, as resolved by the validator.
/// </param>
public sealed record PowerShellValidationResult(
    IReadOnlyList<string> SafeArguments,
    string EffectiveLanguageMode);
