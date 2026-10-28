using TractoRuntime.Browser.Binary;

namespace TractoRuntime.Browser.Packaging;

/// <summary>
/// Production implementation of <see cref="IRuntimePackage"/> (BRW-US-021).
///
/// <para>Provides:</para>
/// <list type="bullet">
///   <item>The pinned Playwright for .NET version and runtime version for capability digests.</item>
///   <item>The list of browser channels bundled with this package (BRW-US-021 AC#3).</item>
///   <item>
///     Isolated-deployment detection: the runtime is considered isolated when the
///     <c>TRACTO_ISOLATED_HOST</c> environment variable equals <c>"true"</c> or <c>"1"</c>,
///     or when the <c>isolated-deployment.marker</c> sentinel file is present next to the
///     runtime binary.  Both signals are checked so that both container-level env-var injection
///     and packaging-step file stamps are honoured (Architecture v1.2A §§5,9).
///   </item>
/// </list>
///
/// <para>
/// In isolated mode the runtime must not attempt any internet download; all browser binaries
/// must be present in the offline bundle (BRW-US-021 AC#2 / BRW-AT-19).
/// </para>
/// </summary>
public sealed class RuntimePackage : IRuntimePackage
{
    // ── Public constants exposed for testing and diagnostics ──────────────────

    /// <summary>
    /// Environment variable that overrides isolated-host detection.
    /// Set to <c>"true"</c> or <c>"1"</c> to force isolated mode regardless of the marker file.
    /// </summary>
    public const string IsolatedHostEnvVar = "TRACTO_ISOLATED_HOST";

    /// <summary>
    /// Pinned Playwright for .NET NuGet package version.
    /// Must match the <c>PackageReference</c> in <c>TractoRuntime.Browser.csproj</c>.
    /// Exposed publicly so the <see cref="OfflineBundleDescriptor"/> can reference it at
    /// construction time without duplicating the version string (BRW-US-021 AC#3).
    /// </summary>
    public const string PinnedPlaywrightVersion = "1.48.0";

    // ── Sentinel file written by the offline packaging step ───────────────────
    private const string IsolatedMarkerFileName = "isolated-deployment.marker";

    // ── IRuntimePackage ───────────────────────────────────────────────────────

    /// <inheritdoc />
    /// <remarks>Semver version of this Browser Runtime build; updated at release time.</remarks>
    public string RuntimeVersion { get; } = "1.0.0";

    /// <inheritdoc />
    /// <remarks>
    /// Returns <see cref="PinnedPlaywrightVersion"/> — the compile-time pinned Playwright for
    /// .NET version.  Always available, including in isolated (air-gapped) deployments.
    /// </remarks>
    public string PlaywrightVersion { get; } = PinnedPlaywrightVersion;

    /// <inheritdoc />
    /// <remarks>
    /// When constructed with a <see cref="BrowserBinaryManifest"/>, the channel list is derived
    /// from that manifest so the advertised channels always match the pinned binary entries.
    /// When constructed without a manifest, the default hard-coded list is used.
    /// </remarks>
    public IReadOnlyList<string> BundledBrowserChannels { get; }

    // ── Constructors ──────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises a <see cref="RuntimePackage"/> deriving the bundled channel list from
    /// the supplied <paramref name="manifest"/>.  The manifest is the authoritative source
    /// for which channels are shipped in this build (BRW-US-021 AC#3).
    /// </summary>
    public RuntimePackage(BrowserBinaryManifest manifest)
    {
        BundledBrowserChannels = [.. manifest.Entries.Select(e => e.Channel)];
    }

    /// <summary>
    /// Initialises a <see cref="RuntimePackage"/> with the default hard-coded channel list.
    /// Used when no manifest is available at construction time (e.g. early boot checks).
    /// </summary>
    public RuntimePackage()
    {
        BundledBrowserChannels = ["chromium", "chromium-headless-shell"];
    }

    // ── Isolated-deployment detection ─────────────────────────────────────────

    /// <inheritdoc />
    /// <remarks>
    /// Detection order:
    /// <list type="number">
    ///   <item>
    ///     <see cref="IsolatedHostEnvVar"/> set to <c>"true"</c> (case-insensitive) or <c>"1"</c>
    ///     → isolated mode.
    ///   </item>
    ///   <item>
    ///     <c>isolated-deployment.marker</c> file present next to the runtime binary
    ///     → isolated mode.
    ///   </item>
    ///   <item>Neither signal present → connected mode.</item>
    /// </list>
    ///
    /// In isolated mode no internet download is attempted at any point; the host-level Supervisor
    /// manages the worker lifecycle as described in Architecture v1.2A §§5,9.
    /// </remarks>
    public Task<bool> IsIsolatedDeploymentAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // 1. Environment variable override (takes precedence over marker file).
        string? envValue = System.Environment.GetEnvironmentVariable(IsolatedHostEnvVar);
        if (string.Equals(envValue, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(envValue, "1", StringComparison.Ordinal))
        {
            return Task.FromResult(true);
        }

        // 2. Marker file written by the offline packaging step.
        string markerPath = Path.Combine(AppContext.BaseDirectory, IsolatedMarkerFileName);
        return Task.FromResult(File.Exists(markerPath));
    }
}
