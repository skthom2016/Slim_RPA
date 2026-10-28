using Tracto.Desktop.Element;

namespace Tracto.Desktop.Controls;

/// <summary>
/// Provides tab-strip navigation — selecting a named tab within a tab control.
/// </summary>
public interface ITabNavigator
{
    /// <summary>
    /// Selects the tab identified by <paramref name="tab"/> within <paramref name="tabStrip"/>.
    /// The operation is idempotent: if the tab is already selected no further event is sent.
    /// </summary>
    /// <exception cref="Tracto.Desktop.Element.DesktopActionError">
    /// Thrown with <c>"TARGET_NOT_FOUND"</c> when no tab with the given name exists.
    /// </exception>
    Task SelectTabAsync(
        ElementRef        tabStrip,
        TabSelector       tab,
        CancellationToken ct = default);
}
