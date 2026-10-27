using Tracto.Desktop.Element;

namespace Tracto.Desktop.Controls;

/// <summary>
/// Handles Windows confirm/prompt dialogs — clicking an approved button or
/// raising <see cref="DesktopActionError"/> when the requested action is not permitted.
/// </summary>
public interface IConfirmDialogHandler
{
    /// <summary>
    /// Clicks the button whose name matches <paramref name="buttonName"/> in the dialog
    /// rooted at <paramref name="dialogEl"/> — e.g. "Save", "OK", "Yes".
    /// </summary>
    /// <exception cref="Tracto.Desktop.Element.DesktopActionError">
    /// Thrown with <c>"TARGET_NOT_FOUND"</c> when the button is not found.
    /// </exception>
    Task ConfirmAsync(
        ElementRef        dialogEl,
        string            buttonName,
        CancellationToken ct = default);

    /// <summary>
    /// Denies (dismisses without executing) the dialog by clicking <paramref name="buttonName"/>.
    /// Any attempt to dismiss a dialog in a way that was not explicitly declared as a "deny"
    /// action must raise <see cref="DesktopActionError"/> with <c>"POLICY_DENIED"</c>.
    /// </summary>
    /// <exception cref="Tracto.Desktop.Element.DesktopActionError">
    /// Thrown with <c>"POLICY_DENIED"</c> when the deny action is not permitted by policy,
    /// or <c>"TARGET_NOT_FOUND"</c> when the button is not found.
    /// </exception>
    Task DenyAsync(
        ElementRef        dialogEl,
        string            buttonName,
        CancellationToken ct = default);
}
