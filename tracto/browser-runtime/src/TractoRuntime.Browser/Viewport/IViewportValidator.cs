namespace TractoRuntime.Browser.Viewport;

/// <summary>
/// Validates viewport settings against the certified viewport list (BRW-007).
/// An unsupported viewport must be rejected before any browser interaction begins.
/// </summary>
public interface IViewportValidator
{
    /// <summary>
    /// Validates that <paramref name="viewport"/> is one of the certified viewport configurations.
    /// </summary>
    /// <exception cref="ViewportNotSupportedException">
    /// Thrown when the viewport is not in the certified list.
    /// The caller must convert this to errorCategory=VALIDATION before launching a browser.
    /// </exception>
    Task ValidateAsync(ViewportSettings viewport, CancellationToken ct);

    /// <summary>Returns the complete list of certified viewport configurations.</summary>
    IReadOnlyList<ViewportSettings> GetCertifiedViewports();
}
