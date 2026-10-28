using Microsoft.Playwright;

namespace TractoRuntime.Browser.Navigation;

/// <summary>
/// Executes navigation operations on a Playwright page, enforcing origin policy
/// and supporting all history operations required by BRW-US-005 AC#8 and WEB-001.
/// </summary>
public interface INavigationExecutor
{
    /// <summary>
    /// Executes the specified navigation on the given page.
    /// </summary>
    /// <param name="page">The Playwright page on which to navigate.</param>
    /// <param name="request">Navigation operation parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="NavigationResult"/> describing whether navigation succeeded and,
    /// on failure, the CEC-conformant error category and detail code.
    /// </returns>
    Task<NavigationResult> ExecuteAsync(IPage page, NavigationRequest request, CancellationToken ct);
}
