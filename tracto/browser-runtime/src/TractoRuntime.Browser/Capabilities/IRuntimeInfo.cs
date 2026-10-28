namespace TractoRuntime.Browser.Capabilities;

/// <summary>
/// Provides Playwright browser engine name and version for capability advertisement.
/// BRW-006 — browser engine/version must be included in the environment metadata
/// reported to the Orchestrator before the first step.
/// </summary>
public interface IRuntimeInfo
{
    /// <summary>
    /// Returns the browser engine name and the Playwright package version.
    /// For example: ("chromium", "1.48.0").
    /// </summary>
    Task<(string Engine, string Version)> GetBrowserInfoAsync(CancellationToken ct);
}
