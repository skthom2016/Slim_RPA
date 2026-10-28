namespace Tracto.TerminalRuntime.Infrastructure.PowerShell;

/// <summary>
/// Configuration section for PowerShell safe-execution policy, bound from appsettings.json.
/// Section name: "TerminalRuntime:PowerShellPolicy".
/// </summary>
public sealed class PowerShellPolicySettings
{
    public const string SectionName = "TerminalRuntime:PowerShellPolicy";

    /// <summary>
    /// Execution policy flag passed to pwsh.exe.
    /// Defaults to "RemoteSigned". "Bypass" and "Unrestricted" are always rejected.
    /// </summary>
    public string ExecutionPolicy { get; set; } = "RemoteSigned";

    /// <summary>
    /// Exhaustive list of PowerShell module names permitted for import.
    /// </summary>
    public string[] AllowedModules { get; set; } = [];

    /// <summary>
    /// Language mode enforced via $ExecutionContext.SessionState.LanguageMode.
    /// Must be "ConstrainedLanguage", "RestrictedLanguage", or "NoLanguage".
    /// </summary>
    public string LanguageMode { get; set; } = "ConstrainedLanguage";

    /// <summary>
    /// When true (development only), script integrity hashes are not enforced.
    /// Must be false in production deployments.
    /// </summary>
    public bool SkipIntegrityCheckInDevelopment { get; set; } = false;

    /// <summary>
    /// Absolute directory paths from which PowerShell scripts are permitted to be loaded.
    /// Any script whose full resolved path does not start with one of these prefixes is
    /// rejected before any file I/O is performed.
    /// Leave empty only in development environments where <see cref="SkipIntegrityCheckInDevelopment"/>
    /// is also true; production deployments must specify at least one approved root.
    /// </summary>
    public string[] ApprovedScriptRoots { get; set; } = [];
}
