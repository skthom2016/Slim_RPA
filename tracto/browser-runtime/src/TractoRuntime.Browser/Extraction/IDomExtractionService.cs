using Microsoft.Playwright;
using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Extraction;

/// <summary>
/// Extracts DOM values as opaque strings for <c>browser.element.getText</c>,
/// <c>browser.element.getAttr</c>, and <c>browser.element.getValue</c> (DOM-009 / BRW-US-010 AC#3).
///
/// Extracted values are forwarded unchanged to <c>service.invoke</c>; no parsing, arithmetic,
/// or domain comparison is performed inside the runtime.
/// </summary>
public interface IDomExtractionService
{
    /// <summary>
    /// Extracts an opaque value from the DOM element identified by <paramref name="selector"/>
    /// using the mode declared in <paramref name="spec"/>.
    /// </summary>
    /// <param name="page">The current Playwright page.</param>
    /// <param name="spec">Extraction mode and output key.</param>
    /// <param name="selector">The locator identifying the DOM element to read.</param>
    /// <param name="timeoutMs">Per-step bounded timeout in milliseconds (BRE-001).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An <see cref="ExtractionResult"/> carrying the raw opaque value, or a structured error
    /// if the element could not be found or the attribute did not exist.
    /// </returns>
    Task<ExtractionResult> ExtractAsync(
        IPage page,
        ExtractionSpec spec,
        SelectorRef? selector,
        float timeoutMs,
        CancellationToken ct);
}

/// <summary>Result of an opaque DOM extraction (DOM-009 / BRW-US-010 AC#3).</summary>
public sealed record ExtractionResult
{
    /// <summary>Whether the extraction succeeded.</summary>
    public required bool Succeeded { get; init; }

    /// <summary>
    /// The raw opaque extracted value passed unchanged to the caller.
    /// Non-null when <see cref="Succeeded"/> is true.
    /// </summary>
    public string? ExtractedValue { get; init; }

    /// <summary>Human-readable error description (non-null when <see cref="Succeeded"/> is false).</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>CEC §6.2 error category (null when <see cref="Succeeded"/> is true).</summary>
    public ErrorCategory? ErrorCategory { get; init; }

    /// <summary>Namespaced <c>browser.*</c> detail code (null when <see cref="Succeeded"/> is true).</summary>
    public string? DetailCode { get; init; }

    /// <summary>Returns a successful extraction result with the given opaque value.</summary>
    public static ExtractionResult Ok(string value)
        => new() { Succeeded = true, ExtractedValue = value };

    /// <summary>Returns a failed extraction result with the given structured error.</summary>
    public static ExtractionResult Fail(string message, ErrorCategory category, string detailCode)
        => new()
        {
            Succeeded     = false,
            ErrorMessage  = message,
            ErrorCategory = category,
            DetailCode    = detailCode
        };
}
