namespace Tracto.Desktop.Element;

/// <summary>
/// Options controlling element-find behaviour (timeout, scroll, enabled-state validation).
/// </summary>
public sealed record ElementFindOptions
{
    public static readonly ElementFindOptions Default = new ElementFindOptions
    {
        TimeoutMs = 5000,
        ScrollIntoView = false,
        ValidateEnabled = true,
    };

    /// <summary>Total milliseconds to poll before giving up with TARGET_NOT_FOUND.</summary>
    public int TimeoutMs { get; init; } = 5000;

    /// <summary>When true, attempt to scroll the element into the visible viewport after finding it.</summary>
    public bool ScrollIntoView { get; init; }

    /// <summary>When true, verify that the resolved element is enabled before returning it.</summary>
    public bool ValidateEnabled { get; init; }
}
