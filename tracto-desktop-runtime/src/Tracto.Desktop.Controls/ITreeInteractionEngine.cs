using Tracto.Desktop.Element;

namespace Tracto.Desktop.Controls;

/// <summary>
/// Provides tree-control interaction — expand and collapse nodes — using
/// Windows UI Automation <c>ExpandCollapsePattern</c>.
/// </summary>
public interface ITreeInteractionEngine
{
    /// <summary>
    /// Expands or collapses the tree node represented by <paramref name="el"/> to match
    /// <paramref name="expanded"/>. The operation is idempotent: if the node is already
    /// in the target state no UIA event is sent.
    /// </summary>
    /// <param name="el">The tree node element to expand or collapse.</param>
    /// <param name="expanded">
    /// <c>true</c> to expand; <c>false</c> to collapse.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="Tracto.Desktop.Element.DesktopActionError">
    /// Thrown with <c>"ENVIRONMENT"</c> when the element does not support
    /// <c>ExpandCollapsePattern</c>.
    /// </exception>
    Task ExpandAsync(
        ElementRef        el,
        bool              expanded,
        CancellationToken ct = default);
}
