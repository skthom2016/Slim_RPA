namespace Tracto.Desktop.Element;

/// <summary>
/// Immutable snapshot of an element's current UIA state, produced by <see cref="IElementStateChecker"/>.
/// </summary>
public sealed record ElementStateResult
{
    /// <summary>Whether the UIA element reports IsEnabled = true.</summary>
    public bool IsEnabled { get; init; }

    /// <summary>Whether the UIA element is on-screen (IsOffscreen = false).</summary>
    public bool IsVisible { get; init; }

    /// <summary>
    /// Whether the element exposes at least one UIA automation pattern (or keyboard path).
    /// False indicates a non-accessible canvas/custom control.
    /// </summary>
    public bool HasProvider { get; init; }

    /// <summary>
    /// Non-null when the element is owned by an elevated-integrity process that denied access.
    /// Value is "POLICY_DENIED" in that case.
    /// </summary>
    public string? AccessDeniedReason { get; init; }
}
