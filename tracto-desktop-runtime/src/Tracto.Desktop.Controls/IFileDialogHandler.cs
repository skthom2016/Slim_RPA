using Tracto.Desktop.Element;

namespace Tracto.Desktop.Controls;

/// <summary>
/// Handles Windows file picker dialogs — typing an approved path into the filename
/// field, confirming and cancelling.
/// </summary>
public interface IFileDialogHandler
{
    /// <summary>
    /// Locates the open/save file dialog (Win32 class "32770"), types <paramref name="filePath"/>
    /// into the filename Edit control using the approved keyboard procedure.
    /// </summary>
    /// <exception cref="Tracto.Desktop.Element.DesktopActionError">
    /// Thrown with <c>"TARGET_NOT_FOUND"</c> when no file dialog is currently visible.
    /// </exception>
    Task TypeFilePathAsync(
        string            filePath,
        CancellationToken ct = default);

    /// <summary>
    /// Clicks the Open or Save button to confirm the dialog selection.
    /// </summary>
    /// <exception cref="Tracto.Desktop.Element.DesktopActionError">
    /// Thrown with <c>"TARGET_NOT_FOUND"</c> when the confirm button is not found.
    /// </exception>
    Task ConfirmAsync(CancellationToken ct = default);

    /// <summary>
    /// Clicks the Cancel button to dismiss the dialog without selecting a file.
    /// </summary>
    Task CancelAsync(CancellationToken ct = default);
}
