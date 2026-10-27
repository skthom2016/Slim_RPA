namespace Tracto.Desktop.Element;

/// <summary>
/// Enforces pre-input element state invariants.
/// Call <see cref="AssertActionable"/> before any mouse or keyboard action.
/// </summary>
public static class ElementStateGuard
{
    /// <summary>
    /// Asserts that the element is in a state where an action may safely proceed.
    /// Throws a <see cref="DesktopActionError"/> describing the single, precise obstacle.
    /// </summary>
    /// <exception cref="DesktopActionError">
    /// <list type="bullet">
    ///   <item>ErrorCategory=ENVIRONMENT, DetailCode=desktop.elementDisabled — element is in tree but disabled.</item>
    ///   <item>ErrorCategory=ENVIRONMENT, DetailCode=desktop.elementOffscreen — element is disabled or off-screen.</item>
    ///   <item>ErrorCategory=ENVIRONMENT, DetailCode=desktop.unsupportedControl — element has no UIA provider.</item>
    ///   <item>ErrorCategory=POLICY_DENIED, DetailCode=desktop.accessDenied — elevated-process boundary.</item>
    /// </list>
    /// </exception>
    public static void AssertActionable(ElementStateResult state)
    {
        // Access-denied check first — it supersedes all other state checks.
        if (state.AccessDeniedReason is not null)
            throw new DesktopActionError("POLICY_DENIED", "desktop.accessDenied");

        // IsEnabled check: single predictable diagnostic for a located-but-disabled control.
        // Callers must NOT receive TARGET_NOT_FOUND for this case (DT-07-AC01).
        if (!state.IsEnabled)
            throw new DesktopActionError("ENVIRONMENT", "desktop.elementDisabled");

        // Visibility check.
        if (!state.IsVisible)
            throw new DesktopActionError("ENVIRONMENT", "desktop.elementOffscreen");

        // Provider check: custom/non-accessible canvas controls.
        if (!state.HasProvider)
            throw new DesktopActionError("ENVIRONMENT", "desktop.unsupportedControl");
    }
}
