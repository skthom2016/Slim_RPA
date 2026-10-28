namespace Tracto.TerminalRuntime.Domain.Policies;

/// <summary>
/// Declares the WSL isolation constraints for a wsl-bash command execution.
/// Carries no framework dependencies (pure domain record).
/// </summary>
/// <param name="CustomerDistro">
///   The WSL distro name that is provisioned for this customer.
///   Every wsl-bash command must target exactly this distro.
/// </param>
/// <param name="CustomerUser">
///   The Linux user that commands must run as inside <see cref="CustomerDistro"/>.
/// </param>
/// <param name="OrchestratorDistro">
///   The distro name used by the Tracto Orchestrator itself.
///   Any command that targets this distro is rejected with POLICY_DENIED (AC-2).
/// </param>
/// <param name="ApprovedScriptRoot">
///   The Linux path prefix inside <see cref="CustomerDistro"/> from which scripts
///   may be invoked. Script paths outside this root are rejected before execution (AC-4).
/// </param>
public sealed record WslPolicy(
    string CustomerDistro,
    string CustomerUser,
    string OrchestratorDistro,
    string ApprovedScriptRoot);
