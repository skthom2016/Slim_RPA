using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Locators;

namespace TractoRuntime.Browser.Host;

/// <summary>
/// Resolves a selector through <see cref="LocatorResolver"/> but, like Playwright's own
/// auto-wait, keeps trying while the target has not appeared yet. Pages that are still
/// navigating (execution context destroyed) are retried too. An ambiguous match is reported
/// at once; a target that never appears within <paramref name="waitMs"/> is reported as
/// not found.
/// </summary>
public sealed class WaitingSelectorStrategy(
    LocatorResolver inner,
    int waitMs,
    ILogger<WaitingSelectorStrategy> logger) : ISelectorStrategy
{
    private const int PollMs = 250;

    public async Task<ILocator> ResolveAsync(IPage page, SelectorRef selectorRef, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(waitMs);
        Exception? last = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await inner.ResolveAsync(page, selectorRef, ct).ConfigureAwait(false);
            }
            catch (LocatorNotFoundException ex)
            {
                last = ex;
            }
            catch (InvalidOperationException ex) when (IsNavigationInFlight(ex))
            {
                last = ex;
                logger.LogDebug("Page is navigating; retrying locator resolution.");
            }

            if (DateTime.UtcNow >= deadline)
                throw last!;

            await Task.Delay(PollMs, ct).ConfigureAwait(false);
        }
    }

    private static bool IsNavigationInFlight(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e.Message.Contains("Execution context was destroyed", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
