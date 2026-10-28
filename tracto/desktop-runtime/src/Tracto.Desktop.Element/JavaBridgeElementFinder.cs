namespace Tracto.Desktop.Element;

/// <summary>
/// Locates Java UI elements via the Java Access Bridge adapter.
/// Enforces the DT-06 policy boundary: a JAB anchor alone is insufficient
/// to derive absolute pointer coordinates; any such attempt raises
/// <see cref="PolicyViolationException"/>.
/// </summary>
public sealed class JavaBridgeElementFinder
{
    private readonly IJavaBridgeProvider _provider;

    public JavaBridgeElementFinder(IJavaBridgeProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <summary>
    /// Resolves a <see cref="JavaBridgeSelector"/> to a <see cref="JavaElement"/> within
    /// <paramref name="parentWindow"/>.
    /// </summary>
    /// <exception cref="DesktopActionError">
    /// Thrown with code "TARGET_NOT_FOUND" when the Java Access Bridge is unavailable
    /// or no matching element is found.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="ct"/> is cancelled before the search completes.
    /// </exception>
    public async Task<JavaElement> FindAsync(
        JavaBridgeSelector sel,
        IntPtr parentWindow,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sel);

        ct.ThrowIfCancellationRequested();

        if (!_provider.IsAvailable())
        {
            throw new DesktopActionError(
                "TARGET_NOT_FOUND",
                "Java Access Bridge not available");
        }

        // Yield briefly to allow cancellation to be observed in async contexts.
        await Task.Yield();
        ct.ThrowIfCancellationRequested();

        var element = _provider.FindByName(parentWindow, sel.AccessibleName, sel.Role);

        if (element is null)
        {
            throw new DesktopActionError(
                "TARGET_NOT_FOUND",
                $"No Java element found with AccessibleName='{sel.AccessibleName}'" +
                (sel.Role is not null ? $" Role='{sel.Role}'" : string.Empty));
        }

        return element;
    }

    /// <summary>
    /// Guard method that enforces the JAB coordinate-input policy.
    /// Calling code that attempts to derive absolute pointer coordinates
    /// from a <see cref="JavaElement"/> alone MUST call this method first.
    /// It always throws to prevent coordinate injection via a JAB anchor.
    /// </summary>
    /// <exception cref="PolicyViolationException">
    /// Always thrown — JAB anchor alone is insufficient for coordinate input (DT-06-AC04).
    /// </exception>
    public static void CoordinateGuard(JavaElement element)
    {
        // Policy: a Java Access Bridge anchor is never sufficient for pointer-coordinate
        // actions. A UIA anchor with a verified bounding rectangle is required.
        // This method is intentionally unconditional.
        throw new PolicyViolationException(
            "JAB anchor insufficient for coordinate input",
            $"Element '{element?.Name}' was resolved via Java Access Bridge. " +
            "A UIA anchor is required for any pointer-coordinate action. " +
            "Use semantic JAB actions (InvokeClick / InvokeTypeText) instead.");
    }
}
