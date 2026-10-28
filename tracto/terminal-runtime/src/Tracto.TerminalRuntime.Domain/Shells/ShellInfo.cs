namespace Tracto.TerminalRuntime.Domain.Shells;

/// <summary>
/// Immutable description of a detected shell executable.
/// Zero framework dependencies — pure domain record.
/// </summary>
/// <param name="ShellType">Which shell family this describes.</param>
/// <param name="ExecutablePath">Absolute path to the executable (empty when unavailable).</param>
/// <param name="Version">Reported version string (empty when unavailable).</param>
/// <param name="IsAvailable">True when the shell was found and its version parsed successfully.</param>
public sealed record ShellInfo(
    ShellType ShellType,
    string ExecutablePath,
    string Version,
    bool IsAvailable)
{
    /// <summary>Convenience factory for an unavailable shell.</summary>
    public static ShellInfo Unavailable(ShellType shellType) =>
        new(shellType, string.Empty, string.Empty, IsAvailable: false);
}
