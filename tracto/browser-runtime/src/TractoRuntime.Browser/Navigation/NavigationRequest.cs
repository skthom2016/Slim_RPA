namespace TractoRuntime.Browser.Navigation;

/// <summary>
/// Describes a single navigation operation to be executed on a Playwright page
/// (BRW-US-005 AC#8, WEB-001).
/// </summary>
public sealed record NavigationRequest
{
    /// <summary>The type of navigation to perform.</summary>
    public required NavigationHistoryAction Action { get; init; }

    /// <summary>
    /// Target absolute URL for <see cref="NavigationHistoryAction.Navigate"/>.
    /// Ignored for Reload, Back, and Forward operations.
    /// </summary>
    public string? Url { get; init; }

    /// <summary>
    /// Declared expected URL that the page must display after navigation completes.
    /// When non-null, the executor asserts an exact case-insensitive URL match before
    /// the run proceeds to the next step (BRW-US-005 AC#8).
    /// Null disables URL assertion.
    /// </summary>
    public string? ExpectedUrl { get; init; }

    /// <summary>
    /// Declared DOM element selector that must be visible after navigation completes.
    /// When non-null, the executor waits for this selector's visibility before proceeding.
    /// Null disables DOM state assertion.
    /// </summary>
    public string? ExpectedSelector { get; init; }

    /// <summary>Maximum time allowed for the navigation to complete.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}
