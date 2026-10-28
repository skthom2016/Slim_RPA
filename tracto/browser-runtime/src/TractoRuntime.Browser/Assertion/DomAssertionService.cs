using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Locators;

namespace TractoRuntime.Browser.Assertion;

/// <summary>
/// Default implementation of <see cref="IDomAssertionService"/>.
///
/// Evaluates the five technical assertion predicates (DOM-008 / BRW-US-010 AC#1):
/// <list type="bullet">
///   <item><b>LocatorVisible</b> — <c>locator.IsVisibleAsync()</c></item>
///   <item><b>LocatorEnabled</b> — <c>locator.IsEnabledAsync()</c></item>
///   <item><b>LocatorChecked</b> — <c>locator.IsCheckedAsync()</c></item>
///   <item><b>UrlEquals</b> — <c>page.Url == spec.ExpectedValue</c> (exact, case-sensitive)</item>
///   <item><b>TextEquals</b> — <c>locator.InnerTextAsync().Trim() == spec.ExpectedValue</c> (exact)</item>
/// </list>
///
/// No domain comparison, arithmetic, or business logic is performed inside this class (AC#2, AC#4).
/// The locator is re-resolved on every call (DOM-006 stale-target semantics).
/// </summary>
public sealed class DomAssertionService(
    ISelectorStrategy selectorStrategy,
    ILogger<DomAssertionService> logger)
    : IDomAssertionService
{
    /// <inheritdoc/>
    public async Task<AssertionResult> EvaluateAsync(
        IPage page,
        AssertionSpec spec,
        SelectorRef? selector,
        float timeoutMs,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        logger.LogDebug(
            "Evaluating DOM assertion kind={Kind} expectedValue={ExpectedValue}.",
            spec.Kind,
            spec.ExpectedValue ?? "(none)");

        return spec.Kind switch
        {
            AssertionKind.LocatorVisible => await EvaluateLocatorVisibleAsync(page, selector, timeoutMs, ct),
            AssertionKind.LocatorEnabled => await EvaluateLocatorEnabledAsync(page, selector, timeoutMs, ct),
            AssertionKind.LocatorChecked => await EvaluateLocatorCheckedAsync(page, selector, timeoutMs, ct),
            AssertionKind.UrlEquals      => EvaluateUrlEquals(page, spec),
            AssertionKind.TextEquals     => await EvaluateTextEqualsAsync(page, selector, spec, timeoutMs, ct),
            _ => AssertionResult.Fail(
                     $"Unknown AssertionKind '{spec.Kind}' — this kind is not in the allowed technical predicate set.",
                     ErrorCategory.VALIDATION,
                     "browser.selectorRefInvalid")
        };
    }

    // ── LocatorVisible (DOM-008 AC#1) ─────────────────────────────────────────

    private async Task<AssertionResult> EvaluateLocatorVisibleAsync(
        IPage page, SelectorRef? selector, float timeoutMs, CancellationToken ct)
    {
        var locator = await ResolveLocatorAsync(page, selector, ct).ConfigureAwait(false);

        // IsVisibleAsync is an immediate snapshot check in Playwright 1.48+.
        // LocatorIsVisibleOptions.Timeout is obsolete and ignored by Playwright;
        // the method does not wait for the element to become visible — it returns
        // the current visibility state synchronously.  timeoutMs has already been
        // applied at the ResolveAsync / WaitForAsync level by the caller when needed.
        _ = timeoutMs; // intentionally unused: IsVisibleAsync ignores timeout options
        var visible = await locator.IsVisibleAsync().ConfigureAwait(false);

        logger.LogDebug("LocatorVisible assertion: visible={Visible}.", visible);

        return visible
            ? AssertionResult.Pass()
            : AssertionResult.Fail(
                "Assertion failed: element is not visible.",
                ErrorCategory.TARGET_NOT_FOUND,
                "browser.locatorNotFound");
    }

    // ── LocatorEnabled (DOM-008 AC#1) ─────────────────────────────────────────

    private async Task<AssertionResult> EvaluateLocatorEnabledAsync(
        IPage page, SelectorRef? selector, float timeoutMs, CancellationToken ct)
    {
        var locator = await ResolveLocatorAsync(page, selector, ct).ConfigureAwait(false);

        var enabled = await locator.IsEnabledAsync(
            new LocatorIsEnabledOptions { Timeout = timeoutMs }).ConfigureAwait(false);

        logger.LogDebug("LocatorEnabled assertion: enabled={Enabled}.", enabled);

        return enabled
            ? AssertionResult.Pass()
            : AssertionResult.Fail(
                "Assertion failed: element is not enabled (disabled).",
                ErrorCategory.TIMEOUT,
                "browser.actionabilityTimeout");
    }

    // ── LocatorChecked (DOM-008 AC#1) ─────────────────────────────────────────

    private async Task<AssertionResult> EvaluateLocatorCheckedAsync(
        IPage page, SelectorRef? selector, float timeoutMs, CancellationToken ct)
    {
        var locator = await ResolveLocatorAsync(page, selector, ct).ConfigureAwait(false);

        var isChecked = await locator.IsCheckedAsync(
            new LocatorIsCheckedOptions { Timeout = timeoutMs }).ConfigureAwait(false);

        logger.LogDebug("LocatorChecked assertion: checked={Checked}.", isChecked);

        return isChecked
            ? AssertionResult.Pass()
            : AssertionResult.Fail(
                "Assertion failed: element is not checked.",
                ErrorCategory.VALIDATION,
                "browser.assertionFailed");
    }

    // ── UrlEquals (DOM-008 AC#1) ──────────────────────────────────────────────

    /// <summary>
    /// Exact URL equality — no parsing, no host extraction, no business comparison.
    /// </summary>
    private AssertionResult EvaluateUrlEquals(IPage page, AssertionSpec spec)
    {
        var currentUrl  = page.Url;
        var expectedUrl = spec.ExpectedValue ?? string.Empty;
        var matches     = string.Equals(currentUrl, expectedUrl, StringComparison.Ordinal);

        logger.LogDebug(
            "UrlEquals assertion: expected='{Expected}' actual='{Actual}' match={Match}.",
            expectedUrl, currentUrl, matches);

        return matches
            ? AssertionResult.Pass()
            : AssertionResult.Fail(
                $"URL assertion failed: expected '{expectedUrl}', actual '{currentUrl}'.",
                ErrorCategory.VALIDATION,
                "browser.assertionFailed");
    }

    // ── TextEquals (DOM-008 AC#1) ─────────────────────────────────────────────

    /// <summary>
    /// Reads the element's <c>innerText</c> and compares it to <see cref="AssertionSpec.ExpectedValue"/>
    /// with an exact, case-sensitive match.  No parsing or domain comparison is performed (AC#4).
    /// </summary>
    private async Task<AssertionResult> EvaluateTextEqualsAsync(
        IPage page,
        SelectorRef? selector,
        AssertionSpec spec,
        float timeoutMs,
        CancellationToken ct)
    {
        var locator = await ResolveLocatorAsync(page, selector, ct).ConfigureAwait(false);

        var rawText      = await locator.InnerTextAsync(
            new LocatorInnerTextOptions { Timeout = timeoutMs }).ConfigureAwait(false);
        var actualText   = rawText.Trim();
        var expectedText = spec.ExpectedValue ?? string.Empty;
        var matches      = string.Equals(actualText, expectedText, StringComparison.Ordinal);

        logger.LogDebug(
            "TextEquals assertion: expected='{Expected}' actual='{Actual}' match={Match}.",
            expectedText, actualText, matches);

        return matches
            ? AssertionResult.Pass()
            : AssertionResult.Fail(
                $"Text assertion failed: expected '{expectedText}', actual '{actualText}'.",
                ErrorCategory.VALIDATION,
                "browser.assertionFailed");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<ILocator> ResolveLocatorAsync(IPage page, SelectorRef? selector, CancellationToken ct)
    {
        if (selector is null)
        {
            throw new InvalidOperationException(
                "Element-scoped assertions (Visible, Enabled, Checked, TextEquals) require a " +
                "Selector to be specified in the step.");
        }

        // DOM-006: fresh locator expression on every call — re-queries the live DOM at assertion time.
        return await selectorStrategy.ResolveAsync(page, selector, ct).ConfigureAwait(false);
    }
}
