namespace Tracto.TerminalRuntime.Domain.Identity;

/// <summary>
/// Declares the execution identity constraints that must be satisfied before a command runs.
/// </summary>
/// <param name="WindowsIdentity">
///   Expected Windows identity (DOMAIN\user or UPN) for cmd/powershell commands.
///   Null means no Windows identity check is required for this policy.
/// </param>
/// <param name="WslDistro">
///   Name of the approved WSL distro for wsl-bash commands.
///   Must not equal <see cref="OrchestratorWslDistro"/>.
/// </param>
/// <param name="WslUser">
///   Linux user that must be effective inside <see cref="WslDistro"/>.
/// </param>
/// <param name="OrchestratorWslDistro">
///   The distro name used by the Orchestrator itself. Any wsl-bash command that
///   targets this distro is rejected with POLICY_DENIED (AC-4).
/// </param>
public sealed record IdentityPolicy(
    string? WindowsIdentity,
    string? WslDistro,
    string? WslUser,
    string? OrchestratorWslDistro);
