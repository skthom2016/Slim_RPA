namespace Tracto.Desktop.Element;

/// <summary>
/// Immutable selector describing how to locate a UIA element by stable properties.
/// No screen-coordinate fields are present; coordinate-based targeting is rejected at validation time.
/// </summary>
public sealed record ElementSelector
{
    public string? AutomationId { get; init; }
    public string? Name { get; init; }
    public string? ClassName { get; init; }
    public string? ControlType { get; init; }

    /// <summary>
    /// Optional ancestor element that scopes the search. The element is searched within this ancestor's subtree.
    /// </summary>
    public ElementSelector? Parent { get; init; }

    /// <summary>
    /// When multiple elements match all other properties, select the one at this zero-based index among siblings.
    /// </summary>
    public int? IndexInParent { get; init; }
}
