using Microsoft.Playwright;
using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Context;

/// <summary>
/// Launches Chromium through Playwright.
/// With no <paramref name="channel"/> the Playwright-bundled Chromium is used; a channel of
/// <c>chrome</c> or <c>msedge</c> launches the browser installed on the machine instead.
/// </summary>
public sealed class PlaywrightBrowserFactory(IPlaywright playwright, string? channel = null) : IBrowserFactory
{
    private readonly string? _channel = string.IsNullOrWhiteSpace(channel) ? null : channel;

    public Task<IBrowser> LaunchAsync(BrowserMode mode, CancellationToken ct)
    {
        var options = new BrowserTypeLaunchOptions
        {
            Headless = mode == BrowserMode.Headless,
            Channel  = _channel
            // No download-path override: binaries must be pre-installed and validated
            // by IBinaryValidator before this factory is called (BRW-001).
        };

        // Playwright .NET does not accept CancellationToken on LaunchAsync; the caller
        // is responsible for imposing a deadline via an overall run timeout.
        return playwright.Chromium.LaunchAsync(options);
    }
}
