namespace Tracto.TerminalRuntime.Infrastructure.OutputBindings;

/// <summary>
/// Options bound from <c>appsettings.json</c> section <c>"EnvAllowlist"</c>.
/// Provides the set of environment variable names that the <c>terminal.env.inspect</c>
/// action is permitted to read.
/// </summary>
public sealed class EnvAllowlistSettings
{
    public const string SectionName = "EnvAllowlist";

    /// <summary>
    /// Allowlisted environment variable names (case-sensitive).
    /// Names absent from this list are denied with <c>errorCategory=POLICY_DENIED</c>.
    /// </summary>
    public string[] AllowedNames { get; set; } = [];

    /// <summary>Returns the allowlist as a read-only set for efficient membership checks.</summary>
    public IReadOnlySet<string> ToSet() =>
        new HashSet<string>(AllowedNames, StringComparer.Ordinal);
}
