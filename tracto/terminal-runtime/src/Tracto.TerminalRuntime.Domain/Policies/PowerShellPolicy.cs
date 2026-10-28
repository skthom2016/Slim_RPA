namespace Tracto.TerminalRuntime.Domain.Policies;

/// <summary>
/// Immutable policy record that governs how PowerShell commands are allowed to run.
/// Declared in deployment configuration and enforced before any PowerShell process launches.
/// </summary>
/// <param name="AllowedModules">
///   Exhaustive whitelist of PowerShell module names that may be imported.
///   Any Import-Module call targeting a name outside this list is rejected.
/// </param>
/// <param name="LanguageMode">
///   The PowerShell language mode to enforce: "ConstrainedLanguage", "RestrictedLanguage",
///   or "NoLanguage". "FullLanguage" is never accepted.
/// </param>
/// <param name="ExecutionPolicy">
///   The execution policy flag passed to pwsh (e.g. "RemoteSigned", "AllSigned").
///   "Bypass" and "Unrestricted" are never accepted regardless of this value.
/// </param>
/// <param name="AllowBypassPolicy">
///   Always false. Embedded as a named field so policy serialization is explicit
///   and any accidental "true" in configuration is caught at validation time.
/// </param>
public sealed record PowerShellPolicy(
    string[] AllowedModules,
    string LanguageMode,
    string ExecutionPolicy = "RemoteSigned",
    bool AllowBypassPolicy = false)
{
    // ── Policy constants ────────────────────────────────────────────────────────

    /// <summary>Language modes that are accepted by policy.</summary>
    public static readonly IReadOnlySet<string> AcceptedLanguageModes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ConstrainedLanguage",
            "RestrictedLanguage",
            "NoLanguage"
        };

    /// <summary>Execution-policy values that are explicitly forbidden.</summary>
    public static readonly IReadOnlySet<string> ForbiddenExecutionPolicies =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Bypass",
            "Unrestricted"
        };

    /// <summary>Cmdlet / alias names whose use is unconditionally prohibited.</summary>
    public static readonly IReadOnlySet<string> ForbiddenCmdlets =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Invoke-Expression",
            "iex",
            "Invoke-Command",  // remote invocation
            "Start-Process",   // arbitrary process launch
            "New-Object"       // COM interop and arbitrary type instantiation
        };
}
