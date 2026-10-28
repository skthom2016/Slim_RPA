using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Locators;

/// <summary>
/// Default implementation of <see cref="IShadowDomLocatorService"/> (DOM-004 / BRW-US-008).
/// </summary>
/// <remarks>
/// <b>Open shadow roots (AC#3).</b>
/// Playwright's <c>GetByLabel</c>, <c>GetByText</c>, <c>GetByTestId</c>, and standard
/// <c>Locator(css)</c> calls automatically pierce open shadow roots via the browser's
/// DevTools protocol.  No additional Playwright API injection is required.
///
/// <b>Closed / inaccessible shadow roots (AC#4).</b>
/// A closed shadow root cannot be accessed by any external script or by Playwright's CDP
/// bridge.  When the action executor reports a timeout for a selector with
/// <c>shadowRoot=true</c>, <see cref="TryClassifyShadowDomError"/> returns a structured
/// <see cref="CecError"/> with <c>detailCode=browser.shadowRootClosed</c> rather than
/// letting the failure appear as a generic element-not-found error.
///
/// <b>Design note.</b>
/// Shadow DOM locator creation is separated from frame chain resolution: a shadow-root
/// element on the top-level page uses this service; a shadow-root element inside an iframe
/// must be resolved by <see cref="IFrameChainResolver"/> first and the resulting
/// <see cref="ILocator"/> (already scoped to the frame) acts as the search root.
/// </remarks>
public sealed class ShadowDomLocatorService(ILogger<ShadowDomLocatorService> logger) : IShadowDomLocatorService
{
    // Playwright timeout / closed-shadow error message fragments.
    // These appear in PlaywrightException.Message when the root cause is an inaccessible shadow.
    private static readonly string[] ShadowRootErrorFragments =
    [
        "closed shadow",
        "shadowroot",
        "shadow-root",
        "Node is detached from document",
        "Element is not attached to the DOM",
    ];

    // -------------------------------------------------------------------------
    // IShadowDomLocatorService — BuildLocator
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>
    /// Playwright's standard locator API pierces open shadow roots automatically.
    /// The explicit <c>shadowRoot=true</c> flag is used by the caller to request
    /// shadow-root error classification in <see cref="TryClassifyShadowDomError"/> —
    /// it does not change the locator strategy here because Playwright already
    /// handles it transparently.
    /// </remarks>
    public ILocator BuildLocator(IPage page, SelectorRef selectorRef)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selectorRef);

        if (!selectorRef.ShadowRoot)
            throw new ArgumentException(
                $"{nameof(BuildLocator)} must only be called when {nameof(SelectorRef.ShadowRoot)} is true.",
                nameof(selectorRef));

        logger.LogDebug("Building shadow-root-aware locator (ShadowRoot=true).");

        // Priority 1 — role + accessible name
        if (selectorRef.Role is not null)
        {
            var opts = new PageGetByRoleOptions();
            if (selectorRef.RoleName is not null)
                opts.Name = selectorRef.RoleName;
            return page.GetByRole(AriaRoleHelper.FromString(selectorRef.Role), opts);
        }

        // Priority 2 — label
        string? label = selectorRef.Label ?? selectorRef.AriaLabel;
        if (label is not null)
            return page.GetByLabel(label);

        // Priority 3 — placeholder
        if (selectorRef.Placeholder is not null)
            return page.GetByPlaceholder(selectorRef.Placeholder);

        // Priority 4 — testId
        if (selectorRef.TestId is not null)
            return page.GetByTestId(selectorRef.TestId);

        // Priority 5 — text
        if (selectorRef.Text is not null)
            return page.GetByText(selectorRef.Text);

        // Priority 6 — CSS (already policy-validated by ArtifactValidator).
        // Standard CSS locators pierce shadow roots in Playwright when the browser
        // exposes them via DevTools (open roots only).
        if (selectorRef.Css is not null)
            return page.Locator(selectorRef.Css);

        // XPath — does NOT pierce shadow roots in browsers; flagged here for clarity.
        if (selectorRef.XPath is not null)
        {
            logger.LogWarning(
                "XPath selector used with ShadowRoot=true. XPath does not pierce shadow roots " +
                "in most browsers; consider using a CSS, label, or role-based selector instead.");
            return page.Locator($"xpath={selectorRef.XPath}");
        }

        throw new ArgumentException(
            "SelectorRef with ShadowRoot=true has no element discriminator. " +
            "Specify at least one of: Role, Label, Placeholder, TestId, Text, Css, or XPath.",
            nameof(selectorRef));
    }

    // -------------------------------------------------------------------------
    // IShadowDomLocatorService — TryClassifyShadowDomError
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public CecError? TryClassifyShadowDomError(PlaywrightException ex, SelectorRef selectorRef)
    {
        ArgumentNullException.ThrowIfNull(ex);
        ArgumentNullException.ThrowIfNull(selectorRef);

        if (!selectorRef.ShadowRoot)
            return null; // Not a shadow-DOM selector; caller handles normally.

        var message = ex.Message ?? string.Empty;

        bool isShadowRootError = ShadowRootErrorFragments.Any(
            fragment => message.Contains(fragment, StringComparison.OrdinalIgnoreCase));

        // A timeout on a shadowRoot=true selector strongly implies the root is closed
        // or inaccessible — Playwright cannot pierce a closed shadow root.
        bool isTimeout = message.Contains("Timeout", StringComparison.OrdinalIgnoreCase)
                      || message.Contains("waiting for", StringComparison.OrdinalIgnoreCase);

        if (isShadowRootError || (selectorRef.ShadowRoot && isTimeout))
        {
            logger.LogWarning(
                "Shadow root appears closed or inaccessible. Classifying as browser.shadowRootClosed. " +
                "Playwright message: {Message}", message);

            return new CecError
            {
                Category  = ErrorCategory.TARGET_NOT_FOUND,
                Message   = "Shadow root is closed or inaccessible. The runtime cannot pierce a " +
                            "closed shadow root. Verify the component exposes an open shadow root " +
                            $"and the selector is correct. Playwright detail: {message}",
                DetailCode = "browser.shadowRootClosed"
            };
        }

        return null; // Exception is not a shadow-root issue; caller handles it.
    }

}
