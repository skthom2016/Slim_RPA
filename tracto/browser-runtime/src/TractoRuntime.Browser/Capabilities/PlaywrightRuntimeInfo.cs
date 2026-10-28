namespace TractoRuntime.Browser.Capabilities;

/// <summary>
/// Production implementation of <see cref="IRuntimeInfo"/>.
/// Returns the pinned Playwright package version declared in the project file.
/// In v1 the certified engine is Chromium; other engines require separate certification.
/// BRW-001 — browser binary version is pinned; no runtime download is permitted.
/// </summary>
public sealed class PlaywrightRuntimeInfo : IRuntimeInfo
{
    // Matches the PackageReference version in TractoRuntime.Browser.csproj.
    private const string PinnedPlaywrightVersion = "1.48.0";
    private const string CertifiedEngine = "chromium";

    public Task<(string Engine, string Version)> GetBrowserInfoAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult((CertifiedEngine, PinnedPlaywrightVersion));
    }
}
