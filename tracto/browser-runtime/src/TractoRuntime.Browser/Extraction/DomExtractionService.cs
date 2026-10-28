using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Locators;

namespace TractoRuntime.Browser.Extraction;

/// <summary>
/// Default implementation of <see cref="IDomExtractionService"/>.
///
/// Reads DOM values as opaque strings using one of the three sanctioned modes (DOM-009 /
/// BRW-US-010 AC#3):
/// <list type="bullet">
///   <item>
///     <b>DisplayedText</b> (<c>browser.element.getText</c>) — <c>locator.InnerTextAsync()</c>.
///     Returns the element's rendered text content, trimmed of leading/trailing whitespace.
///   </item>
///   <item>
///     <b>Attribute</b> (<c>browser.element.getAttr</c>) — <c>locator.GetAttributeAsync(name)</c>.
///     Returns the raw attribute value, or fails if the attribute is absent.
///   </item>
///   <item>
///     <b>FormValue</b> (<c>browser.element.getValue</c>) — <c>locator.InputValueAsync()</c>.
///     Returns the current value of an input, textarea, or select element.
///   </item>
/// </list>
///
/// The extracted value is passed unchanged to the caller; no parsing, arithmetic, or domain
/// comparison is performed inside this class (AC#3).  The locator is re-resolved on every
/// call to respect DOM-006 stale-target semantics.
/// </summary>
public sealed class DomExtractionService(
    ISelectorStrategy selectorStrategy,
    ILogger<DomExtractionService> logger)
    : IDomExtractionService
{
    /// <inheritdoc/>
    public async Task<ExtractionResult> ExtractAsync(
        IPage page,
        ExtractionSpec spec,
        SelectorRef? selector,
        float timeoutMs,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (selector is null)
        {
            return ExtractionResult.Fail(
                "DOM extraction requires a Selector to be specified in the step.",
                ErrorCategory.VALIDATION,
                "browser.selectorRefInvalid");
        }

        // DOM-006: fresh locator expression on every extraction — re-queries the live DOM.
        ILocator locator;
        try
        {
            locator = await selectorStrategy.ResolveAsync(page, selector, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ExtractionResult.Fail(
                $"Failed to resolve locator for extraction: {ex.Message}",
                ErrorCategory.TARGET_NOT_FOUND,
                "browser.locatorNotFound");
        }

        ct.ThrowIfCancellationRequested();

        logger.LogDebug("Extracting DOM value using mode={Mode}.", spec.Mode);

        return spec.Mode switch
        {
            ExtractionMode.DisplayedText => await ExtractDisplayedTextAsync(locator, timeoutMs),
            ExtractionMode.Attribute     => await ExtractAttributeAsync(locator, spec, timeoutMs),
            ExtractionMode.FormValue     => await ExtractFormValueAsync(locator, timeoutMs),
            _ => ExtractionResult.Fail(
                     $"Unknown ExtractionMode '{spec.Mode}'.",
                     ErrorCategory.VALIDATION,
                     "browser.selectorRefInvalid")
        };
    }

    // ── DisplayedText (browser.element.getText / DOM-009 AC#3a) ───────────────

    private async Task<ExtractionResult> ExtractDisplayedTextAsync(ILocator locator, float timeoutMs)
    {
        var rawText = await locator.InnerTextAsync(
            new LocatorInnerTextOptions { Timeout = timeoutMs }).ConfigureAwait(false);

        var text = rawText.Trim();
        logger.LogDebug("DisplayedText extracted ({Length} chars).", text.Length);
        return ExtractionResult.Ok(text);
    }

    // ── Attribute (browser.element.getAttr / DOM-009 AC#3b) ───────────────────

    private async Task<ExtractionResult> ExtractAttributeAsync(
        ILocator locator, ExtractionSpec spec, float timeoutMs)
    {
        if (string.IsNullOrEmpty(spec.AttributeName))
        {
            return ExtractionResult.Fail(
                "ExtractionMode.Attribute requires ExtractionSpec.AttributeName to be a non-empty string.",
                ErrorCategory.VALIDATION,
                "browser.selectorRefInvalid");
        }

        var value = await locator.GetAttributeAsync(
            spec.AttributeName,
            new LocatorGetAttributeOptions { Timeout = timeoutMs }).ConfigureAwait(false);

        if (value is null)
        {
            logger.LogDebug("Attribute '{Name}' not found on element.", spec.AttributeName);
            return ExtractionResult.Fail(
                $"Attribute '{spec.AttributeName}' does not exist on the matched element.",
                ErrorCategory.TARGET_NOT_FOUND,
                "browser.locatorNotFound");
        }

        logger.LogDebug("Attribute '{Name}' extracted ({Length} chars).", spec.AttributeName, value.Length);
        return ExtractionResult.Ok(value);
    }

    // ── FormValue (browser.element.getValue / DOM-009 AC#3c) ──────────────────

    private async Task<ExtractionResult> ExtractFormValueAsync(ILocator locator, float timeoutMs)
    {
        var value = await locator.InputValueAsync(
            new LocatorInputValueOptions { Timeout = timeoutMs }).ConfigureAwait(false);

        logger.LogDebug("FormValue extracted ({Length} chars).", value.Length);
        return ExtractionResult.Ok(value);
    }
}
