using Microsoft.Playwright;
using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Locators;

/// <summary>
/// Provides shadow-root-aware locator construction and closed-shadow-root error
/// classification (DOM-004 / BRW-US-008).
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>AC#3 — Open shadow-root elements are targeted using Playwright's standard
///               locators, which automatically pierce open shadow roots.  No separate
///               Playwright injection is required.</item>
///   <item>AC#4 — When a declared <c>shadowRoot=true</c> selector finds no element
///               within the action timeout, the error is classified as
///               <c>errorCategory=TARGET_NOT_FOUND</c> with
///               <c>detailCode=browser.shadowRootClosed</c> rather than being
///               silently swallowed or mapped to a generic not-found error.</item>
/// </list>
/// </remarks>
public interface IShadowDomLocatorService
{
    /// <summary>
    /// Builds a Playwright locator for an element declared to reside inside an open shadow root.
    /// Playwright's locator API automatically pierces open shadow roots via the browser's
    /// DevTools protocol; this method selects the appropriate locator strategy for the given
    /// element discriminators.
    /// </summary>
    /// <param name="page">The top-level Playwright page. Shadow-root elements inside iframes must first
    /// be resolved by <see cref="IFrameChainResolver"/>; the resulting frame-scoped locator is used as
    /// the search root and this method is not required for that path.</param>
    /// <param name="selectorRef">
    /// Selector with <see cref="SelectorRef.ShadowRoot"/> set to <see langword="true"/>.
    /// </param>
    /// <returns>
    /// A lazy <see cref="ILocator"/> that will pierce the shadow root during action execution.
    /// </returns>
    ILocator BuildLocator(IPage page, SelectorRef selectorRef);

    /// <summary>
    /// Examines a <see cref="PlaywrightException"/> that occurred while acting on an element
    /// with <see cref="SelectorRef.ShadowRoot"/> set to <see langword="true"/> and determines
    /// whether it originated from a closed or inaccessible shadow root.
    /// </summary>
    /// <param name="ex">The Playwright exception to classify.</param>
    /// <param name="selectorRef">The selector that was being resolved when the exception occurred.</param>
    /// <returns>
    /// A <see cref="CecError"/> with <c>errorCategory=TARGET_NOT_FOUND</c> and
    /// <c>detailCode=browser.shadowRootClosed</c> when the failure appears to be a closed/
    /// inaccessible shadow root, or <see langword="null"/> when the exception has a different cause.
    /// </returns>
    CecError? TryClassifyShadowDomError(PlaywrightException ex, SelectorRef selectorRef);
}
