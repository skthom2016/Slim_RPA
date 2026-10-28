using Tracto.TerminalRuntime.Domain.Policies;

namespace Tracto.TerminalRuntime.Application.PowerShell;

/// <summary>
/// Command that carries everything needed to validate a PowerShell invocation
/// against deployment policy before any process is launched.
/// </summary>
/// <param name="Policy">
///   The PowerShell policy from deployment configuration.
/// </param>
/// <param name="ScriptPath">
///   Absolute path to the .ps1 script to be executed.
///   Must be a pre-approved, versioned script reference — inline scripts are not accepted.
/// </param>
/// <param name="Arguments">
///   Typed argument key/value pairs for the script.
///   Values are validated to contain no shell metacharacters or injection sequences.
/// </param>
/// <param name="ExtraCommandLineArgs">
///   Any additional raw arguments the caller intends to pass to pwsh.exe.
///   Inspected for forbidden flags (e.g. -ExecutionPolicy Bypass).
/// </param>
/// <param name="ModulesToImport">
///   Optional list of module names the script intends to import.
///   Each name is checked against <see cref="PowerShellPolicy.AllowedModules"/>.
/// </param>
/// <param name="ScriptIntegrityHash">
///   Expected SHA-256 hash of the script file content (hex, lowercase).
///   Null means integrity checking is skipped (only allowed in development policy).
/// </param>
public sealed record ValidatePowerShellCommandCommand(
    PowerShellPolicy Policy,
    string ScriptPath,
    IReadOnlyDictionary<string, string> Arguments,
    IReadOnlyList<string> ExtraCommandLineArgs,
    IReadOnlyList<string> ModulesToImport,
    string? ScriptIntegrityHash);
