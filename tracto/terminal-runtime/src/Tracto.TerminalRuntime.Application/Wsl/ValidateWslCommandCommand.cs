using Tracto.TerminalRuntime.Domain.Policies;

namespace Tracto.TerminalRuntime.Application.Wsl;

/// <summary>
/// Command that carries all data needed to validate a wsl-bash invocation
/// before any process is launched.
/// </summary>
/// <param name="DeclaredDistro">
///   The WSL distro name declared by the step definition.
///   Must equal <see cref="WslPolicy.CustomerDistro"/> (AC-3).
/// </param>
/// <param name="DeclaredUser">
///   The Linux user declared by the step definition.
/// </param>
/// <param name="ScriptPath">
///   The absolute Linux path to the script inside the distro (e.g. /opt/tracto/run.sh).
///   Must be within <see cref="WslPolicy.ApprovedScriptRoot"/> (AC-4).
/// </param>
/// <param name="ScriptArgs">
///   Optional arguments forwarded verbatim to the script via ArgumentList.
/// </param>
/// <param name="Policy">
///   The provisioned WSL policy for this tenant/worker.
/// </param>
public sealed record ValidateWslCommandCommand(
    string DeclaredDistro,
    string DeclaredUser,
    string ScriptPath,
    IReadOnlyList<string> ScriptArgs,
    WslPolicy Policy);
