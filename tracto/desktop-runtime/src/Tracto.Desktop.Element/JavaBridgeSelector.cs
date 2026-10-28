namespace Tracto.Desktop.Element;

/// <summary>
/// Selector for locating a Java UI element via the Java Access Bridge.
/// Uses stable semantic properties: accessible name, role and optional description.
/// No screen coordinates are present — JAB anchor alone does not authorize coordinate input.
/// </summary>
public sealed record JavaBridgeSelector
{
    /// <summary>
    /// The accessible name of the Java control (e.g. "Submit", "Username").
    /// </summary>
    public required string AccessibleName { get; init; }

    /// <summary>
    /// Optional accessible role filter (e.g. "push button", "text", "tree").
    /// When null, any role is accepted.
    /// </summary>
    public string? Role { get; init; }

    /// <summary>
    /// Optional accessible description for additional disambiguation.
    /// When null, description is ignored during matching.
    /// </summary>
    public string? Description { get; init; }
}
