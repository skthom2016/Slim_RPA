namespace Tracto.TerminalRuntime.Domain.Policies;

/// <summary>
/// Immutable policy record that declares which patterns are forbidden in command templates.
///
/// <para>
/// <b>ForbiddenPatterns</b> — regex patterns that detect business-logic predicates
/// (e.g. amount comparisons, threshold checks) that must never appear in a command template.
/// </para>
/// <para>
/// <b>ForbiddenNetworkTools</b> — regex patterns that identify shell-based HTTP / SQL
/// network tools (curl, wget, Invoke-WebRequest, Invoke-RestMethod, sqlcmd, …) whose
/// direct use in a command template bypasses the sanctioned service.invoke channel.
/// </para>
/// </summary>
public sealed record CommandTemplateSafetyPolicy(
    string[] ForbiddenPatterns,
    string[] ForbiddenNetworkTools)
{
    /// <summary>
    /// Default production policy.  Covers the business-predicate patterns and
    /// network-tool names mandated by CEC v1.0.1 invariant #5 and #7.
    /// </summary>
    public static readonly CommandTemplateSafetyPolicy Default = new(
        ForbiddenPatterns:
        [
            // Numeric threshold comparisons — PowerShell operators
            @"-gt\s+\d+",
            @"-lt\s+\d+",
            @"-ge\s+\d+",
            @"-le\s+\d+",
            @"-eq\s+\d+",
            @"-ne\s+\d+",
            // Bash / sh numeric comparisons
            @"\[\s*\$\w+\s*-gt\s*\d+",
            @"\[\s*\$\w+\s*-lt\s*\d+",
            @"\[\s*\$\w+\s*-ge\s*\d+",
            @"\[\s*\$\w+\s*-le\s*\d+",
            // Python / JS numeric comparisons involving variable names and literals
            @"\b\w+\s*>\s*\d{4,}",
            @"\b\w+\s*<\s*\d{4,}",
            @"\b\w+\s*>=\s*\d{4,}",
            @"\b\w+\s*<=\s*\d{4,}",
        ],
        ForbiddenNetworkTools:
        [
            // Shell HTTP clients
            @"\bcurl\b",
            @"\bwget\b",
            // PowerShell HTTP cmdlets
            @"\bInvoke-WebRequest\b",
            @"\bInvoke-RestMethod\b",
            // SQL clients / database tools
            @"\bsqlcmd\b",
            @"\bosql\b",
            @"\bmysql\b",
            @"\bpsql\b",
            // Python / Node one-liners that hit the network
            @"\burllib\b",
            @"\brequests\b",
            @"\baxios\b",
            // Targets Python/JS call syntax: fetch(...) — avoids false-positives on "git fetch"
            @"\bfetch\s*\(",
        ]);

    /// <summary>Empty policy — allows all templates (test / override use only).</summary>
    public static readonly CommandTemplateSafetyPolicy AllowAll =
        new(ForbiddenPatterns: [], ForbiddenNetworkTools: []);
}
