namespace Tracto.Desktop.Element;

/// <summary>
/// Queries the live UIA state of a located element.
/// </summary>
public interface IElementStateChecker
{
    /// <summary>
    /// Returns a snapshot of <paramref name="el"/>'s current state.
    /// </summary>
    /// <exception cref="DesktopActionError">
    /// Thrown when the element is irrecoverably unavailable (stale reference).
    /// </exception>
    Task<ElementStateResult> CheckAsync(ElementRef el, CancellationToken ct);
}
