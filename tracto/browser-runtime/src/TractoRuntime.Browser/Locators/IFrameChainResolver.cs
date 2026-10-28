using Microsoft.Playwright;
using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Locators;

/// <summary>
/// Traverses an explicit nested iframe chain and returns an <see cref="ILocator"/>
/// scoped to the innermost frame, ready for element interaction (DOM-003 / BRW-US-008).
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>AC#1 — The resolved locator is scoped to the innermost frame identified by
///               the <see cref="SelectorRef.FrameChain"/>; each entry is traversed
///               in order from the top-level page.</item>
///   <item>AC#2 — Re-resolution after internal frame navigation is handled automatically
///               by Playwright's lazy <c>FrameLocator</c> — the locator re-resolves the
///               frame on every action without requiring explicit polling.</item>
///   <item>AC#5 — Cross-origin frame access violations are surfaced as structured errors
///               (<c>errorCategory=POLICY_DENIED</c>, <c>detailCode=browser.crossOriginFrame</c>)
///               rather than silently ignored; the runtime does not attempt to bypass them.</item>
/// </list>
/// </remarks>
public interface IFrameChainResolver
{
    /// <summary>
    /// Resolves the frame chain declared on <paramref name="selectorRef"/> and returns
    /// an <see cref="ILocator"/> scoped to the innermost frame.
    ///
    /// <paramref name="selectorRef"/> must satisfy <see cref="SelectorRef.HasFrameChain"/>.
    /// At least one element discriminator (Label, Text, TestId, Role, Css) must also be set
    /// so the locator can identify the specific element within the frame.
    /// </summary>
    /// <param name="page">Top-level Playwright page.</param>
    /// <param name="selectorRef">Selector carrying the frame chain and element locator.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A Playwright <see cref="ILocator"/> pointing at the declared element inside the
    /// innermost frame.  The locator is lazy; cross-origin or chain errors surface
    /// when an action is first performed on it.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="selectorRef"/> has no frame chain or no element discriminator.
    /// </exception>
    Task<ILocator> ResolveAsync(IPage page, SelectorRef selectorRef, CancellationToken ct);
}
