using Tracto.Desktop.Element;

namespace Tracto.Desktop.Controls;

/// <summary>
/// Provides hierarchical menu navigation — walking a menu path by invoking each
/// item in sequence via <c>InvokePattern</c> or hover-to-expand.
/// </summary>
public interface IMenuNavigator
{
    /// <summary>
    /// Navigates the menu rooted at <paramref name="menuRoot"/> following the steps
    /// in <paramref name="path"/>, invoking each entry in sequence.
    /// </summary>
    /// <exception cref="Tracto.Desktop.Element.DesktopActionError">
    /// Thrown with <c>"TARGET_NOT_FOUND"</c> when a step name has no matching menu item.
    /// </exception>
    Task NavigateAsync(
        ElementRef        menuRoot,
        MenuPath          path,
        CancellationToken ct = default);
}
