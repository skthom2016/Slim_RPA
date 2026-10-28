using Tracto.TerminalRuntime.Application.Common;

namespace Tracto.TerminalRuntime.Application.Wsl;

/// <summary>
/// Validates a wsl-bash command against the provisioned <see cref="ValidateWslCommandCommand.Policy"/>
/// and, on success, constructs the <c>wsl.exe</c> argument list.
///
/// Enforcement order (all checks run before any process is launched):
/// <list type="number">
///   <item>AC-2: <c>declaredDistro == OrchestratorDistro</c> → POLICY_DENIED.</item>
///   <item>AC-3: <c>declaredDistro != CustomerDistro</c> → POLICY_DENIED.</item>
///   <item>AC-4: <c>scriptPath</c> outside <c>ApprovedScriptRoot</c> → POLICY_DENIED.</item>
///   <item>AC-1: All checks pass → returns <see cref="WslValidationResult"/> with argument list.</item>
/// </list>
/// </summary>
public interface IWslCommandValidator
{
    /// <summary>
    /// Validates <paramref name="command"/> against its embedded policy and, on success,
    /// returns a <see cref="WslValidationResult"/> containing the <c>wsl.exe</c> argument list.
    /// </summary>
    Result<WslValidationResult, Error> Validate(ValidateWslCommandCommand command);
}
