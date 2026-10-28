namespace TractoRuntime.Browser.Binary;

/// <summary>
/// Pinned browser binary descriptor loaded from the signed offline bundle manifest
/// (<c>browser-manifest.json</c> embedded in the runtime package or sidecar file).
/// Immutable once loaded; never modified at runtime.
/// </summary>
public sealed record BrowserBinaryEntry
{
    /// <summary>Browser channel identifier, e.g. "chromium", "firefox", "webkit".</summary>
    public required string Channel { get; init; }

    /// <summary>Expected Playwright revision string (e.g. "1148"} for the pinned build.</summary>
    public required string Revision { get; init; }

    /// <summary>
    /// Lowercase hex-encoded SHA-256 digest of the browser executable file.
    /// Must match before any run is allowed to start.
    /// </summary>
    public required string Sha256Hex { get; init; }

    /// <summary>
    /// Relative path inside the offline bundle (or absolute path for connected-mode installs).
    /// The validator resolves this against the runtime base directory when <paramref name="binaryPath"/>
    /// is not supplied explicitly.
    /// </summary>
    public required string RelativePath { get; init; }

    /// <summary>Human-readable version string reported in the capability digest.</summary>
    public string? DisplayVersion { get; init; }
}

/// <summary>
/// Top-level manifest shipped with every Browser Runtime build.
/// Deserialized once at startup; all fields are treated as immutable.
/// </summary>
public sealed record BrowserBinaryManifest
{
    /// <summary>Manifest schema version — must be "1.0".</summary>
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>Channel that this worker advertises as its primary engine.</summary>
    public required string DefaultChannel { get; init; }

    /// <summary>All pinned binary entries declared in this bundle.</summary>
    public required IReadOnlyList<BrowserBinaryEntry> Entries { get; init; }

    /// <summary>Returns the entry for <paramref name="channel"/>, or <c>null</c> if not found.</summary>
    public BrowserBinaryEntry? FindEntry(string channel) =>
        Entries.FirstOrDefault(e => string.Equals(e.Channel, channel, StringComparison.OrdinalIgnoreCase));
}
