namespace Tracto.TerminalRuntime.Domain.Arguments;

/// <summary>
/// Declares the set of URL hosts and filesystem path roots that arguments are
/// permitted to reference.  An argument whose value resembles a URL or filesystem
/// path is rejected at schema validation time unless it matches one of these entries.
/// </summary>
/// <param name="AllowedHosts">
///   Approved URL hostnames (e.g. "api.internal", "storage.myorg.com").
///   Comparison is case-insensitive.
/// </param>
/// <param name="AllowedPathRoots">
///   Approved filesystem root prefixes (e.g. "C:\data\uploads", "/var/jobs").
///   Comparison is case-insensitive on Windows, ordinal on Linux/macOS.
/// </param>
public sealed record AllowedUrlRootsPolicy(
    string[] AllowedHosts,
    string[] AllowedPathRoots)
{
    /// <summary>A policy that allows nothing — safe default for use in tests.</summary>
    public static AllowedUrlRootsPolicy DenyAll { get; } = new([], []);
}
