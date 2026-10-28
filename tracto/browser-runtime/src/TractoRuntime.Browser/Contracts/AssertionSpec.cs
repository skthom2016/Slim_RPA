namespace TractoRuntime.Browser.Contracts;

/// <summary>
/// Technical assertion predicates accepted by <c>browser.page.assert</c> (DOM-008 / BRW-US-010 AC#1).
///
/// Only the five listed technical predicates are permitted.  Business-comparison predicates
/// (numeric thresholds, price comparisons, arithmetic expressions) are rejected at artifact
/// validation time before any browser interaction (AC#2 / BRW-AT-14).
/// </summary>
public enum AssertionKind
{
    /// <summary>
    /// The element identified by the step's <see cref="BrowserStep.Selector"/> is currently
    /// visible in the DOM (DOM-008 AC#1).
    /// </summary>
    LocatorVisible,

    /// <summary>
    /// The element identified by the step's <see cref="BrowserStep.Selector"/> is not disabled
    /// (DOM-008 AC#1).
    /// </summary>
    LocatorEnabled,

    /// <summary>
    /// The checkbox or radio button identified by the step's <see cref="BrowserStep.Selector"/>
    /// is in the checked state (DOM-008 AC#1).
    /// </summary>
    LocatorChecked,

    /// <summary>
    /// The current page URL exactly equals <see cref="AssertionSpec.ExpectedValue"/>
    /// (DOM-008 AC#1, exact match).
    /// </summary>
    UrlEquals,

    /// <summary>
    /// The displayed technical text of the element identified by the step's
    /// <see cref="BrowserStep.Selector"/> exactly equals <see cref="AssertionSpec.ExpectedValue"/>
    /// (DOM-008 AC#1, exact case-sensitive match).
    /// </summary>
    TextEquals
}

/// <summary>
/// Declares a technical assertion predicate for the <c>browser.page.assert</c> action (DOM-008 /
/// BRW-US-010).
///
/// <para>
/// Only the five technical predicates defined by <see cref="AssertionKind"/> are accepted.
/// Business predicates (e.g. numeric thresholds, arithmetic expressions, domain comparisons)
/// are rejected by the condition parser at artifact validation time, before any browser
/// interaction (AC#2 / BRW-AT-14).
/// </para>
///
/// <para>
/// When <see cref="RawExpression"/> is non-null the predicate is treated as a business or
/// arithmetic expression and rejected unconditionally (BRW-AT-14).  This field is provided
/// so that test fixtures can supply expressions such as <c>"price &gt; 100"</c> to verify
/// the rejection path; it must be null for any valid technical assertion.
/// </para>
/// </summary>
public sealed record AssertionSpec
{
    /// <summary>
    /// The kind of technical assertion to evaluate.
    /// Must be one of the five allowed <see cref="AssertionKind"/> values.
    /// </summary>
    public required AssertionKind Kind { get; init; }

    /// <summary>
    /// For <see cref="AssertionKind.UrlEquals"/> and <see cref="AssertionKind.TextEquals"/>:
    /// the exact string to compare against.
    /// Ignored (and should be null) for locator-state predicates (Visible, Enabled, Checked).
    /// </summary>
    public string? ExpectedValue { get; init; }

    /// <summary>
    /// When non-null, the condition parser unconditionally rejects this spec as a business or
    /// arithmetic predicate.  Use in test fixtures to represent expressions such as
    /// <c>"price &gt; 100"</c> (DOM-008 AC#2 / BRW-AT-14).
    /// Must be null for any valid technical assertion.
    /// </summary>
    public string? RawExpression { get; init; }
}

/// <summary>
/// Opaque DOM extraction modes for <c>browser.element.getText</c>,
/// <c>browser.element.getAttr</c>, and <c>browser.element.getValue</c> (DOM-009 / BRW-US-010 AC#3).
///
/// Each mode reads one piece of data from the DOM and passes it unchanged to the caller;
/// no runtime parsing, arithmetic, or domain decision is performed inside the runtime.
/// </summary>
public enum ExtractionMode
{
    /// <summary>
    /// Extracts the element's visible/displayed text (<c>innerText</c>).
    /// Corresponds to the action <c>browser.element.getText</c> (DOM-009 AC#3a).
    /// </summary>
    DisplayedText,

    /// <summary>
    /// Extracts a named element attribute value (e.g. <c>href</c>, <c>aria-label</c>).
    /// Corresponds to the action <c>browser.element.getAttr</c> (DOM-009 AC#3b).
    /// Requires <see cref="ExtractionSpec.AttributeName"/> to be set.
    /// </summary>
    Attribute,

    /// <summary>
    /// Extracts the element's form value (input <c>value</c> property, select selected option value).
    /// Corresponds to the action <c>browser.element.getValue</c> (DOM-009 AC#3c).
    /// </summary>
    FormValue
}

/// <summary>
/// Declares an opaque DOM extraction for one of the <c>browser.element.*</c> extraction
/// actions (DOM-009 / BRW-US-010 AC#3).
///
/// <para>
/// The extracted value is stored under <see cref="OutputKey"/> in the step's evidence
/// dictionary and forwarded unchanged to the next <c>service.invoke</c> call.
/// No parsing, arithmetic, or domain comparison is performed by the runtime.
/// </para>
/// </summary>
public sealed record ExtractionSpec
{
    /// <summary>Extraction mode — determines which DOM API is used to read the value.</summary>
    public required ExtractionMode Mode { get; init; }

    /// <summary>
    /// When <see cref="Mode"/> is <see cref="ExtractionMode.Attribute"/>, the name of the
    /// DOM attribute to read (e.g. <c>"href"</c>, <c>"aria-label"</c>, <c>"data-status"</c>).
    /// Must be a non-empty string for Attribute mode; ignored for other modes.
    /// </summary>
    public string? AttributeName { get; init; }

    /// <summary>
    /// Key under which the extracted opaque value is stored in the step's evidence dictionary
    /// for downstream forwarding to <c>service.invoke</c>.  Defaults to <c>"extractedValue"</c>.
    /// </summary>
    public string OutputKey { get; init; } = "extractedValue";
}
