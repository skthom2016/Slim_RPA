using Tracto.Desktop.Element;

namespace Tracto.Desktop.Controls;

/// <summary>
/// Provides grid and list interaction operations — row selection, cell editing and
/// programmatic scrolling — over Windows UI Automation grid/table patterns.
/// </summary>
public interface IGridInteractionEngine
{
    /// <summary>
    /// Selects the row identified by <paramref name="selector"/> within <paramref name="gridEl"/>.
    /// Row identity is resolved by cell content when <see cref="GridRowSelector.MatchCellValue"/>
    /// is set, making selection stable across sort and filter operations.
    /// </summary>
    /// <exception cref="Tracto.Desktop.Element.DesktopActionError">
    /// Thrown with <c>"TARGET_NOT_FOUND"</c> when no row matches the selector,
    /// or <c>"ENVIRONMENT"</c> when the grid does not expose the required UIA patterns.
    /// </exception>
    Task SelectRowAsync(
        ElementRef      gridEl,
        GridRowSelector selector,
        CancellationToken ct = default);

    /// <summary>
    /// Enters edit mode on the cell at (<paramref name="row"/>, <paramref name="column"/>),
    /// sets <paramref name="newValue"/> and confirms the edit.
    /// </summary>
    /// <exception cref="Tracto.Desktop.Element.DesktopActionError">
    /// Thrown with <c>"ENVIRONMENT"</c> when the cell is read-only.
    /// </exception>
    Task EditCellAsync(
        ElementRef      gridEl,
        int             row,
        int             column,
        string          newValue,
        CancellationToken ct = default);

    /// <summary>
    /// Scrolls the grid to make the row identified by <paramref name="selector"/> visible,
    /// realizing virtualized rows via <c>VirtualizedItemPattern</c> where needed.
    /// </summary>
    /// <exception cref="Tracto.Desktop.Element.DesktopActionError">
    /// Thrown with <c>"TIMEOUT"</c> when realization exceeds the bounded timeout,
    /// or <c>"ENVIRONMENT"</c> with <c>detailCode=desktop.unsupportedControl</c> when the
    /// grid does not support programmatic scrolling.
    /// </exception>
    Task ScrollToRowAsync(
        ElementRef      gridEl,
        GridRowSelector selector,
        TimeSpan        timeout,
        CancellationToken ct = default);
}
