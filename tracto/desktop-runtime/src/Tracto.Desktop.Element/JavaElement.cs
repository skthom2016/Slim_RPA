namespace Tracto.Desktop.Element;

/// <summary>
/// Represents a Java UI element resolved via the Java Access Bridge.
/// Carries semantic identity and bounding rectangle information.
/// A JAB element alone does NOT authorize coordinate-based pointer input —
/// a UIA anchor is always required for pointer actions (DT-06-AC04).
/// </summary>
public sealed record JavaElement
{
    /// <summary>
    /// Handle of the top-level Java application window that owns this element.
    /// </summary>
    public required IntPtr WindowHandle { get; init; }

    /// <summary>
    /// Accessible name of the element as reported by the Java Access Bridge.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Accessible role of the element (e.g. "push button", "text", "tree").
    /// </summary>
    public required string Role { get; init; }

    /// <summary>Left edge of the element's bounding rectangle (screen coordinates).</summary>
    public int X { get; init; }

    /// <summary>Top edge of the element's bounding rectangle (screen coordinates).</summary>
    public int Y { get; init; }

    /// <summary>Width of the element's bounding rectangle in pixels.</summary>
    public int Width { get; init; }

    /// <summary>Height of the element's bounding rectangle in pixels.</summary>
    public int Height { get; init; }
}
