using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Policies;

namespace Tracto.TerminalRuntime.Application.PowerShell;

/// <summary>
/// Application-layer handler for <see cref="ValidatePowerShellCommandCommand"/>.
/// Orchestrates policy pre-checks (fast-fail) then delegates deep validation to
/// <see cref="IPowerShellPolicyValidator"/> (Infrastructure). Contains no process-launching logic.
/// </summary>
public sealed class ValidatePowerShellCommandHandler(IPowerShellPolicyValidator validator)
{
    /// <summary>
    /// Validates the PowerShell command against deployment policy.
    /// Returns <see cref="PowerShellValidationResult"/> on success, or a typed failure on rejection.
    /// </summary>
    public async Task<Result<PowerShellValidationResult, Error>> HandleAsync(
        ValidatePowerShellCommandCommand command,
        CancellationToken ct = default)
    {
        // ── Fast-fail: AllowBypassPolicy must always be false ─────────────────
        if (command.Policy.AllowBypassPolicy)
            return Result.Fail<PowerShellValidationResult>(
                Error.PolicyDenied(
                    "PS_POLICY_001",
                    "PowerShell policy AllowBypassPolicy must be false; the deployment configuration is invalid.",
                    detailCode: "powershell.bypassPolicyEnabled"));

        // ── Fast-fail: LanguageMode must be in the accepted set ───────────────
        if (!PowerShellPolicy.AcceptedLanguageModes.Contains(command.Policy.LanguageMode))
            return Result.Fail<PowerShellValidationResult>(
                Error.PolicyDenied(
                    "PS_POLICY_002",
                    $"Language mode '{command.Policy.LanguageMode}' is not permitted. Accepted: {string.Join(", ", PowerShellPolicy.AcceptedLanguageModes)}.",
                    detailCode: "powershell.forbiddenLanguageMode"));

        // ── Fast-fail: script path must be provided ───────────────────────────
        if (string.IsNullOrWhiteSpace(command.ScriptPath))
            return Result.Fail<PowerShellValidationResult>(
                Error.Validation(
                    "PS_POLICY_003",
                    "Script path must be provided. Inline PowerShell scripts are not permitted.",
                    detailCode: "powershell.missingScriptPath"));

        // ── Delegate remaining checks (arg scanning, module allowlist, integrity) to Infrastructure
        return await validator.ValidateAsync(command, ct);
    }
}
