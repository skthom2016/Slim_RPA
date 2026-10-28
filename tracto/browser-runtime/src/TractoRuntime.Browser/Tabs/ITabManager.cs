using Microsoft.Playwright;

namespace TractoRuntime.Browser.Tabs;

/// <summary>
/// Manages tab and popup lifecycle for a single run (BRW-US-012, WEB-004).
/// <list type="bullet">
///   <item>AC#1 — Detects and links expected popups to the run's browser context.</item>
///   <item>AC#2 — Treats any undeclared popup as an error; never silently closes or accepts it.</item>
/// </list>
/// </summary>
public interface ITabManager : IAsyncDisposable
{
    /// <summary>
    /// Registers a popup declaration so the next matching new-page event is linked
    /// to the run rather than treated as unexpected (AC#1).
    /// Must be called BEFORE the browser action that opens the popup.
    /// </summary>
    void DeclareExpectedPopup(PopupDeclaration declaration);

    /// <summary>
    /// Installs event listeners on <paramref name="context"/> so that new pages
    /// are automatically classified as expected or unexpected.
    /// Must be called once, immediately after the context is created for the run.
    /// </summary>
    void Install(IBrowserContext context);

    /// <summary>
    /// Returns the <see cref="IPage"/> associated with <paramref name="popupId"/>,
    /// waiting up to <paramref name="ct"/> / the caller's timeout for it to arrive
    /// (AC#1 — the runtime can switch to it and continue automation).
    /// Throws <see cref="InvalidOperationException"/> if the popup was never declared.
    /// </summary>
    Task<IPage> SwitchToPopupAsync(string popupId, TimeSpan timeout, CancellationToken ct);

    /// <summary>
    /// Returns <see langword="true"/> when an undeclared popup was detected since the
    /// last call to <see cref="ConsumeUnexpectedPopupError"/>.
    /// </summary>
    bool HasUnexpectedPopup { get; }

    /// <summary>
    /// Returns and clears the undeclared-popup error (AC#2).
    /// Returns <see langword="null"/> when no unexpected popup has been detected.
    /// </summary>
    (string url, string message)? ConsumeUnexpectedPopupError();
}
