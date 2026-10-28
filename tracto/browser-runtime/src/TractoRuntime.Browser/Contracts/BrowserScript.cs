using TractoRuntime.Browser.Dialogs;
using TractoRuntime.Browser.Tabs;
using TractoRuntime.Browser.Viewport;

namespace TractoRuntime.Browser.Contracts;

public sealed record BrowserScript
{
    public required string ScriptId { get; init; }
    public required string Version { get; init; }
    public required IReadOnlyList<BrowserStep> Steps { get; init; }
    public BrowserMode Mode { get; init; } = BrowserMode.Headless;
    public TimeSpan DefaultStepTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan RunTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Declared viewport for this script (BRW-007).
    /// When null the runtime uses <see cref="ViewportSettings.Default"/> (1280x720 @ 1x).
    /// A non-null value must match a certified entry; unsupported viewports are rejected
    /// before any browser interaction with errorCategory=VALIDATION.
    /// </summary>
    public ViewportSettings? Viewport { get; init; }
}

public sealed record BrowserStep
{
    public required string StepId { get; init; }
    public required string Action { get; init; }
    public SelectorRef? Selector { get; init; }
    public string? Value { get; init; }
    public string? SecretRef { get; init; }
    public OutputRedaction OutputRedaction { get; init; } = OutputRedaction.Full;
    public TimeSpan? Timeout { get; init; }
    public ServiceInvokeRef? ServiceRef { get; init; }

    /// <summary>
    /// Secondary target selector used by drag-and-drop (<c>browser.element.dragdrop</c>)
    /// to identify the drop target.  <see cref="Selector"/> is the drag source.
    /// (WEB-003 / BRW-US-011 AC#6)
    /// </summary>
    public SelectorRef? TargetSelector { get; init; }

    // BRW-US-013 — file transfer parameters
    /// <summary>
    /// Download parameters; required for <c>browser.file.download</c> steps (WEB-007).
    /// </summary>
    public DownloadRef? DownloadRef { get; init; }

    /// <summary>
    /// Upload parameters; required for <c>browser.file.upload</c> steps (WEB-006).
    /// </summary>
    public UploadRef? UploadRef { get; init; }

    // ── BRW-US-012 extensions ────────────────────────────────────────────────

    /// <summary>
    /// For <c>browser.tab.declare</c>: registers an expected popup/tab before the triggering action.
    /// For <c>browser.tab.switch</c>: <see cref="Value"/> holds the <c>PopupId</c> to switch to.
    /// (WEB-004 / BRW-US-012 AC#1, AC#2)
    /// </summary>
    public PopupDeclaration? PopupRef { get; init; }

    /// <summary>
    /// For <c>browser.dialog.arm</c>: arms a one-shot dialog response before the triggering action.
    /// For <c>browser.dialog.respond</c>: both arms the response and optionally triggers via
    /// <see cref="Selector"/> click.
    /// (WEB-005 / BRW-US-012 AC#3, AC#4)
    /// </summary>
    public DialogDeclaration? DialogRef { get; init; }

    // ── BRW-US-010 — Technical Assertion and Safe DOM Inspection ─────────────

    /// <summary>
    /// Technical assertion predicate for <c>browser.page.assert</c> (DOM-008 / BRW-US-010 AC#1).
    /// Must be non-null for assert steps and null for all other action kinds.
    /// Business-comparison predicates are rejected at artifact validation time by the condition
    /// parser before any browser interaction (AC#2 / BRW-AT-14).
    /// </summary>
    public AssertionSpec? Assertion { get; init; }

    /// <summary>
    /// Opaque DOM extraction specification for <c>browser.element.getText</c>,
    /// <c>browser.element.getAttr</c>, and <c>browser.element.getValue</c>
    /// (DOM-009 / BRW-US-010 AC#3).
    /// The extracted value is forwarded unchanged to <c>service.invoke</c>; no runtime
    /// parsing or domain comparison is performed.  Must be null for non-extraction actions.
    /// </summary>
    public ExtractionSpec? Extraction { get; init; }
}

/// <summary>
/// Identifies one iframe in a nested frame chain (DOM-003 / BRW-US-008).
/// At least one discriminator must be set; the runtime uses the first non-null
/// discriminator in the order: Css → Name → UrlPattern → Index.
/// </summary>
public sealed record FrameRef
{
    /// <summary>
    /// CSS selector targeting the &lt;iframe&gt; or &lt;frame&gt; element itself
    /// (e.g. "iframe#payment", "iframe.checkout-widget").
    /// </summary>
    public string? Css { get; init; }

    /// <summary>
    /// Value of the frame's <c>name</c> attribute
    /// (e.g. &lt;iframe name="checkout"&gt;).
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Substring pattern matched against the frame's <c>src</c> URL
    /// (e.g. "payments.example.com").  Matched using <c>src*=</c> CSS attribute selector.
    /// </summary>
    public string? UrlPattern { get; init; }

    /// <summary>
    /// Zero-based position index when no other discriminator uniquely identifies the frame.
    /// Use as a last resort; prefer a stable Css/Name/UrlPattern when available.
    /// </summary>
    public int? Index { get; init; }

    /// <summary>Returns a Playwright-compatible CSS selector string for this frame entry.</summary>
    internal string ToCssSelector() => this switch
    {
        { Css: { } css }           => css,
        { Name: { } name }         => $"iframe[name='{EscapeAttr(name)}']",
        { UrlPattern: { } pattern } => $"iframe[src*='{EscapeAttr(pattern)}']",
        { Index: { } idx }         => $"iframe:nth-of-type({idx + 1})",   // CSS nth is 1-indexed
        _                          => throw new InvalidOperationException(
                                          "FrameRef must specify at least one of: Css, Name, UrlPattern, or Index.")
    };

    private static string EscapeAttr(string value) =>
        value.Replace("'", "\\'", StringComparison.Ordinal);
}

/// <summary>
/// Typed locator reference resolved in priority order per spec §9 (F-08):
/// role &gt; label &gt; placeholder &gt; testId &gt; text &gt; css (allowCss required) &gt; xpath.
/// CSS selectors require <c>allowCss=true</c> in the deployment policy and are rejected
/// at artifact validation time if the policy flag is absent.
///
/// Extended for BRW-US-008 (DOM-003/DOM-004): a <see cref="FrameChain"/> array drives
/// nested iframe traversal; <see cref="ShadowRoot"/> declares shadow-root targeting.
/// </summary>
public sealed record SelectorRef
{
    /// <summary>Priority 1 — ARIA role string (e.g. "button", "textbox"). Pair with <see cref="RoleName"/> for accessible-name narrowing.</summary>
    public string? Role { get; init; }

    /// <summary>Accessible name paired with <see cref="Role"/> (e.g. "Submit").</summary>
    public string? RoleName { get; init; }

    /// <summary>Priority 2 — Associated form label text (Playwright <c>GetByLabel</c>). Also exposed as <see cref="AriaLabel"/> for backward compatibility.</summary>
    public string? Label { get; init; }

    /// <summary>Backward-compatible alias for <see cref="Label"/>. Prefer <see cref="Label"/> in new scripts.</summary>
    public string? AriaLabel { get; init; }

    /// <summary>Priority 3 — Input placeholder text (Playwright <c>GetByPlaceholder</c>).</summary>
    public string? Placeholder { get; init; }

    /// <summary>Priority 4 — <c>data-testid</c> attribute value (policy-allowlisted).</summary>
    public string? TestId { get; init; }

    /// <summary>Priority 5 — Visible text content, exact match by default (Playwright <c>GetByText</c>).</summary>
    public string? Text { get; init; }

    /// <summary>Priority 6 — CSS selector. Requires <c>allowCss=true</c> in deployment policy; rejected at validation time otherwise.</summary>
    public string? Css { get; init; }

    /// <summary>XPath expression (supported as pragmatic fallback; lower priority than CSS).</summary>
    public string? XPath { get; init; }

    // ------------------------------------------------------------------
    // BRW-US-008 — Frame and Shadow DOM extensions (DOM-003 / DOM-004)
    // ------------------------------------------------------------------

    /// <summary>
    /// Ordered list of frame locators that forms the traversal path from the top-level page
    /// to the innermost iframe that contains the target element (DOM-003).
    /// <list type="bullet">
    ///   <item>Empty / null — the element is on the top-level page (no frame traversal).</item>
    ///   <item>One entry — element is in a single iframe.</item>
    ///   <item>Multiple entries — element is in a nested iframe chain (e.g. outer → inner).</item>
    /// </list>
    /// Cross-origin iframe restrictions enforced by the browser remain in place.
    /// The runtime does not attempt to bypass them (AC#5).
    /// </summary>
    public IReadOnlyList<FrameRef>? FrameChain { get; init; }

    /// <summary>
    /// When <see langword="true"/>, the target element is expected to reside inside an
    /// open shadow root (DOM-004 / AC#3).
    /// Playwright automatically pierces open shadow roots; a closed or inaccessible
    /// shadow root fails with <c>errorCategory=TARGET_NOT_FOUND</c> and
    /// <c>detailCode=browser.shadowRootClosed</c> (AC#4).
    /// </summary>
    public bool ShadowRoot { get; init; }

    /// <summary>Returns <see langword="true"/> when the selector declares a non-empty frame chain.</summary>
    public bool HasFrameChain => FrameChain is { Count: > 0 };
}

public sealed record ServiceInvokeRef
{
    public required string ServiceId { get; init; }
    public Dictionary<string, object?> InputMapping { get; init; } = new();
}

// ──────────────────────────────────────────────────────────────────────────────
// BRW-US-013 — File Download and Upload (WEB-006 / WEB-007)
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Parameters for a <c>browser.file.download</c> step (WEB-007 / BRW-US-013).
/// <list type="bullet">
///   <item><b>AC#1</b> — Filename, content-type (extension-based), and size are validated
///         after the download completes; the step fails before returning Succeeded=true if
///         any declared constraint is violated.</item>
///   <item><b>AC#2</b> — <see cref="DestinationDirectory"/> accepts Unicode paths.</item>
///   <item><b>AC#3</b> — The destination directory is checked against the approved download
///         roots before any bytes are written.</item>
/// </list>
/// </summary>
public sealed record DownloadRef
{
    /// <summary>
    /// Approved destination directory where the downloaded file will be saved.
    /// Must be under an approved download root configured in the deployment policy.
    /// </summary>
    public required string DestinationDirectory { get; init; }

    /// <summary>
    /// Optional expected filename from the server's <c>Content-Disposition</c> header.
    /// When set, the download is rejected if the suggested filename does not match.
    /// Case-insensitive comparison.
    /// </summary>
    public string? ExpectedFilename { get; init; }

    /// <summary>
    /// Optional expected MIME type (e.g., <c>text/csv</c>, <c>application/pdf</c>).
    /// Validated via file-extension mapping because Playwright's <c>IDownload</c> API does
    /// not expose the server-sent Content-Type header directly.
    /// </summary>
    public string? ExpectedContentType { get; init; }

    /// <summary>
    /// Maximum allowed file size in bytes after the download completes.
    /// Zero (the default) means no size limit is enforced.
    /// </summary>
    public long MaxSizeBytes { get; init; }
}

/// <summary>
/// Parameters for a <c>browser.file.upload</c> step (WEB-006 / BRW-US-013).
/// <list type="bullet">
///   <item><b>AC#4</b> — The source path is checked against the approved upload roots
///         before any file transfer or browser interaction begins.</item>
/// </list>
/// </summary>
public sealed record UploadRef
{
    /// <summary>
    /// Approved path to the file that will be uploaded.
    /// Must reside under an approved upload root configured in the deployment policy.
    /// </summary>
    public required string SourcePath { get; init; }
}

public enum BrowserMode
{
    Headed,
    Headless
}

/// <summary>outputRedaction — "none" is NOT a valid value per spec.</summary>
public enum OutputRedaction
{
    Full,
    Partial
}
