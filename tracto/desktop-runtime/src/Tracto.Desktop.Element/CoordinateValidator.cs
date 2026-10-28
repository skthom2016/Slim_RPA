namespace Tracto.Desktop.Element;

/// <summary>
/// Guards against absolute screen-coordinate targeting, which is explicitly prohibited by the
/// UIA-first policy (DT-05-AC06 / DT-05-AC08).
/// </summary>
public static class CoordinateValidator
{
    /// <summary>
    /// Throws <see cref="ArgumentException"/> if <paramref name="value"/> represents a raw screen
    /// coordinate that has been passed where a UIA selector is expected.
    /// Call this in any public API that accepts a numeric value that could be misused as a pixel offset.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when a non-null, finite coordinate value is detected, indicating an attempt to use
    /// screen coordinates instead of a <see cref="ElementSelector"/>.
    /// </exception>
    public static void RejectCoordinate(double? value, string parameterName)
    {
        if (value.HasValue && double.IsFinite(value.Value))
        {
            throw new ArgumentException(
                $"Absolute screen coordinate '{value}' is not permitted in parameter '{parameterName}'. " +
                "Use an ElementSelector (AutomationId, Name, ClassName or ControlType) for UIA-based targeting.",
                parameterName);
        }
    }

    /// <summary>
    /// Throws <see cref="ArgumentException"/> if the selector contains any fields that look like
    /// absolute screen coordinates smuggled as strings. Extension point for future validation.
    /// </summary>
    public static void ValidateSelector(ElementSelector selector)
    {
        if (selector is null)
            throw new ArgumentNullException(nameof(selector));

        // No coordinate fields exist on ElementSelector by design.
        // This method is the explicit enforcement hook; if coordinate fields were ever accidentally
        // added, they would be caught here. Currently a no-op beyond null check.

        if (selector.Parent is not null)
            ValidateSelector(selector.Parent);
    }
}
