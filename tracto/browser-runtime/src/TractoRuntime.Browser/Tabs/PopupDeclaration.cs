namespace TractoRuntime.Browser.Tabs;

/// <summary>
/// Declares an expected popup or tab that will be opened by a page action.
/// Must be registered via <see cref="ITabManager.DeclareExpectedPopup"/> BEFORE
/// the step that triggers the popup fires (WEB-004 / BRW-US-012 AC#1).
/// </summary>
public sealed record PopupDeclaration
{
    /// <summary>
    /// Stable identifier referenced by subsequent <c>browser.tab.switch</c> steps.
    /// Must be unique within the run.
    /// </summary>
    public required string PopupId { get; init; }

    /// <summary>
    /// Optional substring that the popup's URL must contain for the declaration
    /// to be considered matched.  When <see langword="null"/>, the first unclaimed
    /// new popup is matched to this declaration in arrival order.
    /// </summary>
    public string? ExpectedUrlContains { get; init; }

    /// <summary>
    /// Human-readable label used in evidence and log output.
    /// </summary>
    public string? Label { get; init; }
}
