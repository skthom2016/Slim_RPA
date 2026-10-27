namespace Tracto.Desktop.Element;

/// <summary>
/// Abstraction over the Java Access Bridge native library.
/// Implementations wrap WindowsAccessBridge-64.dll via P/Invoke or provide mock behaviour.
/// </summary>
public interface IJavaBridgeProvider
{
    /// <summary>
    /// Returns true when the Java Access Bridge DLL is available and has been successfully
    /// initialised. When false, no JAB calls must be attempted.
    /// </summary>
    bool IsAvailable();

    /// <summary>
    /// Searches the accessibility tree rooted at <paramref name="window"/> for the first
    /// element whose accessible name matches <paramref name="name"/> and, when supplied,
    /// whose role matches <paramref name="role"/>.
    /// </summary>
    /// <param name="window">Top-level Java window handle.</param>
    /// <param name="name">Accessible name to match (case-insensitive).</param>
    /// <param name="role">Optional role filter. Null means any role.</param>
    /// <returns>The matched element, or null if not found.</returns>
    JavaElement? FindByName(IntPtr window, string name, string? role);

    /// <summary>
    /// Fires the accessible default action on <paramref name="el"/> (equivalent to a user
    /// click via the accessibility API). This is NOT a coordinate-based pointer injection.
    /// </summary>
    void InvokeClick(JavaElement el);

    /// <summary>
    /// Delivers <paramref name="text"/> to <paramref name="el"/> using the accessibility
    /// text interface or keyboard events. No absolute coordinate injection is performed.
    /// </summary>
    void InvokeTypeText(JavaElement el, string text);
}
