namespace Tracto.Desktop.Controls;

/// <summary>
/// Represents a sequence of menu item names that forms a navigation path through
/// a hierarchical menu structure (e.g. File → Save As → PDF).
/// </summary>
public sealed class MenuPath
{
    /// <summary>
    /// Ordered array of accessible item names, from the top-level menu entry to the
    /// terminal command. Contains at least one element.
    /// </summary>
    public string[] Steps { get; }

    /// <summary>Initialises a <see cref="MenuPath"/> with the given steps.</summary>
    /// <param name="steps">At least one step is required.</param>
    public MenuPath(string[] steps)
    {
        if (steps is null || steps.Length == 0)
            throw new ArgumentException("MenuPath must contain at least one step.", nameof(steps));
        Steps = steps;
    }
}
