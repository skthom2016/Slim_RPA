using Tracto.TerminalRuntime.Domain.Shells;

namespace Tracto.TerminalRuntime.Application.Shells;

/// <summary>
/// Snapshot of detected shell capabilities sent to the Orchestrator during worker start-up.
/// Immutable — recreated each time the worker restarts.
/// </summary>
public sealed record ShellCapabilityAdvertisement(
    IReadOnlyList<ShellInfo> AvailableShells,
    DateTimeOffset DetectedAt)
{
    /// <summary>Returns the <see cref="ShellInfo"/> for <paramref name="shellType"/>, or null if not found.</summary>
    public ShellInfo? For(ShellType shellType) =>
        AvailableShells.FirstOrDefault(s => s.ShellType == shellType);

    /// <summary>True when the requested shell was detected and is available.</summary>
    public bool Supports(ShellType shellType) =>
        AvailableShells.Any(s => s.ShellType == shellType && s.IsAvailable);
}
