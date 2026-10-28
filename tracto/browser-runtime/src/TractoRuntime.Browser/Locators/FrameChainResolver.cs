using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Locators;

/// <summary>
/// Default implementation of <see cref="IFrameChainResolver"/> (DOM-003 / BRW-US-008).
/// </summary>
/// <remarks>
/// <b>AC#1 — Nested iframe chain.</b>
/// The resolver traverses every <see cref="FrameRef"/> in the <see cref="SelectorRef.FrameChain"/>
/// by calling <c>page.FrameLocator(selector)</c> for the first entry and
/// <c>frameLocator.FrameLocator(selector)</c> for each subsequent entry.  The final
/// <c>IFrameLocator</c> is used to create a locator for the target element.
///
/// <b>AC#2 — Re-resolution after internal frame navigation.</b>
/// Playwright's <c>IFrameLocator</c> is lazy: it re-resolves the underlying frame object
/// on every action.  No additional polling or explicit re-resolution is required.
///
/// <b>AC#5 — Cross-origin restrictions.</b>
/// The implementation does not attempt to bypass browser cross-origin security constraints.
/// If Playwright surfaces a cross-origin access violation when an action is performed on
/// the returned locator, the caller must classify it using <c>errorCategory=POLICY_DENIED</c>
/// with <c>detailCode=browser.crossOriginFrame</c>.
/// </remarks>
public sealed class FrameChainResolver(ILogger<FrameChainResolver> logger) : IFrameChainResolver
{
    /// <inheritdoc/>
    public Task<ILocator> ResolveAsync(IPage page, SelectorRef selectorRef, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!selectorRef.HasFrameChain)
            throw new ArgumentException(
                "SelectorRef does not declare a frame chain. " +
                "Call ISelectorStrategy for top-level element resolution.",
                nameof(selectorRef));

        var chain = selectorRef.FrameChain!;

        // Build the nested IFrameLocator chain.
        // AC#1: traverse from the top-level page downward through each declared iframe.
        IFrameLocator frameLocator = page.FrameLocator(chain[0].ToCssSelector());

        logger.LogDebug(
            "Frame chain step 0: selector={Selector}", chain[0].ToCssSelector());

        for (int i = 1; i < chain.Count; i++)
        {
            var selector = chain[i].ToCssSelector();
            frameLocator = frameLocator.FrameLocator(selector);

            logger.LogDebug("Frame chain step {Index}: selector={Selector}", i, selector);
        }

        // Resolve the element inside the innermost frame.
        var locator = ResolveElementInFrame(frameLocator, selectorRef);

        logger.LogDebug(
            "Frame chain resolved ({Depth} frame(s)). Element locator created for step.", chain.Count);

        // IFrameLocator creation is synchronous in Playwright .NET.
        return Task.FromResult(locator);
    }

    // -------------------------------------------------------------------------
    // Element resolution within a frame — mirrors the priority order used by
    // the semantic locator strategy for top-level elements (DOM-001/002).
    // -------------------------------------------------------------------------

    private static ILocator ResolveElementInFrame(IFrameLocator frameLocator, SelectorRef sel)
    {
        // Priority 1 — role + accessible name
        if (sel.Role is not null)
        {
            var roleOptions = new FrameLocatorGetByRoleOptions();
            if (sel.RoleName is not null)
                roleOptions.Name = sel.RoleName;

            return frameLocator.GetByRole(AriaRoleHelper.FromString(sel.Role), roleOptions);
        }

        // Priority 2 — label / aria-label
        string? label = sel.Label ?? sel.AriaLabel;
        if (label is not null)
            return frameLocator.GetByLabel(label);

        // Priority 3 — placeholder
        if (sel.Placeholder is not null)
            return frameLocator.GetByPlaceholder(sel.Placeholder);

        // Priority 4 — testId
        if (sel.TestId is not null)
            return frameLocator.GetByTestId(sel.TestId);

        // Priority 5 — visible text
        if (sel.Text is not null)
            return frameLocator.GetByText(sel.Text);

        // Priority 6 — CSS (validated upstream; must be present only if allowCss policy is set)
        if (sel.Css is not null)
            return frameLocator.Locator(sel.Css);

        // XPath fallback
        if (sel.XPath is not null)
            return frameLocator.Locator($"xpath={sel.XPath}");

        throw new ArgumentException(
            "SelectorRef has a frame chain but no element discriminator (Role, Label, Placeholder, " +
            "TestId, Text, Css, or XPath). At least one is required to locate the element within the frame.",
            nameof(sel));
    }
}
