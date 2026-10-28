using Microsoft.Playwright;
using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Context;

/// <summary>
/// Production implementation of <see cref="IBrowserFactory"/> that launches a real
/// Playwright browser process.  In isolated-host mode the binary must already be present
/// at the pinned path — no runtime download is attempted (BRW-001 / BRW-US-021).
/// </summary>
public sealed class PlaywrightBrowserFactory(IPlaywright playwright) : IBrowserFactory
{
    public Task<IBrowser> LaunchAsync(BrowserMode mode, CancellationToken ct)
    {
        var options = new BrowserTypeLaunchOptions
        {
            Headless = mode == BrowserMode.Headless
            // No download-path override: binaries must be pre-installed and validated
            // by IBinaryValidator before this factory is called (BRW-001).
        };

        // Playwright .NET does not accept CancellationToken on LaunchAsync; the caller
        // is responsible for imposing a deadline via an overall run timeout.
        return playwright.Chromium.LaunchAsync(options);
    }
}
