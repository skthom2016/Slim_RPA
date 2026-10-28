using Microsoft.Playwright;
using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Context;

/// <summary>
/// Abstracts browser launching so BrowserContextManager can be unit-tested
/// without a real Playwright installation.
/// </summary>
public interface IBrowserFactory
{
    /// <summary>Launches a browser process in the requested mode.</summary>
    Task<IBrowser> LaunchAsync(BrowserMode mode, CancellationToken ct);
}
