using Tracto.TerminalRuntime.Domain.Shells;

namespace Tracto.TerminalRuntime.Domain.Commands;

/// <summary>
/// Immutable registry entry describing a pre-approved command.
///
/// <list type="bullet">
///   <item><c>CommandRef</c>   — logical identifier used in job definitions.</item>
///   <item><c>ExecutablePath</c> — absolute path to the script or binary on disk.</item>
///   <item><c>ExpectedSha256</c> — hex-encoded SHA-256 digest of the executable;
///         Infrastructure verifies this before any process is launched.</item>
///   <item><c>IsApproved</c>  — when false the entry is present but disabled.</item>
///   <item><c>SupportedShells</c> — shells permitted to invoke this command;
///         a request using a different shell is rejected as wrong-runtime.</item>
/// </list>
/// </summary>
public sealed record CommandRegistryEntry(
    string CommandRef,
    string ExecutablePath,
    string ExpectedSha256,
    bool IsApproved,
    ShellType[] SupportedShells,
    /// <summary>
    /// When <see langword="true"/> the command template explicitly supports scripts
    /// that spawn background child processes (Bash <c>&amp;</c>, PowerShell Start-Job, etc.).
    /// Defaults to <see langword="false"/>; background patterns are rejected at launch
    /// time unless this flag is set (TER-US-015 AC-7).
    /// </summary>
    bool AllowsBackground = false);
