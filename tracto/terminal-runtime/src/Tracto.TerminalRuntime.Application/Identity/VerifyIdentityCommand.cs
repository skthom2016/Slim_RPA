using Tracto.TerminalRuntime.Domain.Identity;

namespace Tracto.TerminalRuntime.Application.Identity;

/// <summary>
/// Carries the data needed to verify execution identity before a command runs.
/// </summary>
/// <param name="Policy">
///   The identity constraints declared in the job definition.
/// </param>
/// <param name="CommandType">
///   Shell type string from the job — e.g. "cmd", "powershell", "wsl-bash".
///   Used to select Windows vs. WSL verification path.
/// </param>
public sealed record VerifyIdentityCommand(
    IdentityPolicy Policy,
    string CommandType);
