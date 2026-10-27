using Tracto.Desktop.Element;
using Tracto.Desktop.Input;

namespace Tracto.Desktop.Controls;

/// <summary>
/// Production implementation of <see cref="IFileDialogHandler"/>.
/// Finds the open/save file dialog by Win32 class "32770", types into the filename
/// Edit control and invokes the confirm/cancel button.
/// </summary>
public sealed class FileDialogHandler : IFileDialogHandler
{
    private readonly IUiaPatternProvider  _patterns;
    private readonly IKeyboardInputEngine _keyboard;

    private const string DialogClassName = "32770";
    private const string OpenButtonName  = "Open";
    private const string SaveButtonName  = "Save";
    private const string CancelButtonName = "Cancel";

    public FileDialogHandler(IUiaPatternProvider patterns, IKeyboardInputEngine keyboard)
    {
        _patterns = patterns ?? throw new ArgumentNullException(nameof(patterns));
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
    }

    /// <inheritdoc/>
    public async Task TypeFilePathAsync(
        string            filePath,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        ElementRef dialog = RequireDialog();
        ElementRef? edit  = _patterns.FindFirstEdit(dialog);

        if (edit is null)
            throw new DesktopActionError("TARGET_NOT_FOUND",
                "desktop.fileDialogEditNotFound");

        await _keyboard.TypeAsync(new TypeOptions(edit, filePath), ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task ConfirmAsync(CancellationToken ct = default)
    {
        ElementRef dialog = RequireDialog();

        // Try "Open" first, then "Save".
        ElementRef? button =
            _patterns.FindButtonByName(dialog, OpenButtonName) ??
            _patterns.FindButtonByName(dialog, SaveButtonName);

        if (button is null)
            throw new DesktopActionError("TARGET_NOT_FOUND",
                "desktop.fileDialogConfirmButtonNotFound");

        _patterns.InvokeElement(button);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task CancelAsync(CancellationToken ct = default)
    {
        ElementRef dialog = RequireDialog();

        ElementRef? button = _patterns.FindButtonByName(dialog, CancelButtonName);

        if (button is null)
            throw new DesktopActionError("TARGET_NOT_FOUND",
                "desktop.fileDialogCancelButtonNotFound");

        _patterns.InvokeElement(button);
        return Task.CompletedTask;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ElementRef RequireDialog()
    {
        ElementRef? dialog = _patterns.FindWindowByClass(DialogClassName);
        if (dialog is null)
            throw new DesktopActionError("TARGET_NOT_FOUND",
                "desktop.fileDialogNotFound");
        return dialog;
    }
}
