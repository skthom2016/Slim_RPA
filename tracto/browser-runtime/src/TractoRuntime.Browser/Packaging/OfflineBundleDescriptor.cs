using TractoRuntime.Browser.Binary;

namespace TractoRuntime.Browser.Packaging;

/// <summary>
/// Describes the content of an isolated offline Browser Runtime bundle (BRW-US-021 AC#3).
///
/// An offline bundle is a self-contained deployment package that:
/// <list type="bullet">
///   <item>Declares the pinned Playwright for .NET version used by this runtime build.</item>
///   <item>References the <see cref="BrowserBinaryManifest"/> that lists every bundled browser channel.</item>
///   <item>Records the base directory on disk where the bundled browser executables reside.</item>
/// </list>
///
/// The record is immutable once constructed and is consumed by <see cref="IOfflineBundleValidator"/>
/// before any run is allowed to start.  No field may be mutated at runtime.
/// </summary>
public sealed record OfflineBundleDescriptor
{
    /// <summary>
    /// Pinned Playwright for .NET package version included in this bundle.
    /// Must be non-empty — the absence of a version string means the bundle is incomplete
    /// (BRW-US-021 AC#3 / BRW-AT-19).
    /// </summary>
    public required string PlaywrightVersion { get; init; }

    /// <summary>
    /// Manifest listing all browser channels and their pinned binaries.
    /// Must contain at least one entry (validated by <see cref="BrowserBinaryManifestLoader"/>).
    /// </summary>
    public required BrowserBinaryManifest BinaryManifest { get; init; }

    /// <summary>
    /// Base directory on disk where the bundled browser executable files reside.
    /// Binary paths in <see cref="BrowserBinaryEntry.RelativePath"/> are resolved relative to
    /// this directory unless they are already absolute paths.
    /// </summary>
    public required string BundleBaseDirectory { get; init; }
}
