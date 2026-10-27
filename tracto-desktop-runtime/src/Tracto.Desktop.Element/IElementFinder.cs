namespace Tracto.Desktop.Element;

/// <summary>
/// Finds and re-resolves UIA elements by selector.
/// </summary>
public interface IElementFinder
{
    /// <summary>
    /// Finds an element matching <paramref name="sel"/> within the timeout specified by <paramref name="opts"/>.
    /// </summary>
    /// <exception cref="DesktopActionError">
    /// ErrorCode="TARGET_NOT_FOUND" when no matching element is found within the timeout.
    /// ErrorCode="TARGET_AMBIGUOUS" when more than one matching element exists and no disambiguation is available.
    /// </exception>
    Task<ElementRef> FindAsync(ElementSelector sel, ElementFindOptions opts, CancellationToken ct);

    /// <summary>
    /// Re-runs the original selector from scratch, never using the cached RuntimeId of <paramref name="stale"/>.
    /// Use this when <see cref="ElementRef.IsAlive"/> returns false.
    /// </summary>
    /// <exception cref="DesktopActionError">
    /// ErrorCode="TARGET_NOT_FOUND" when the element can no longer be located.
    /// </exception>
    Task<ElementRef> ReResolveAsync(ElementRef stale, ElementFindOptions opts, CancellationToken ct);
}
