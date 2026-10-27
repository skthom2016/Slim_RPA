namespace Tracto.Desktop.Input;

/// <summary>
/// Thin abstraction over the Win32 clipboard API surface.
/// Implementations may be real (P/Invoke) or test doubles.
/// </summary>
public interface IClipboardApiProvider
{
    /// <summary>
    /// Attempts to open the clipboard for exclusive access.
    /// </summary>
    /// <param name="owner">HWND of the window that owns the clipboard, or <see cref="IntPtr.Zero"/>.</param>
    /// <returns><c>true</c> if the clipboard was opened successfully; <c>false</c> if it is held by another process.</returns>
    bool TryOpenClipboard(IntPtr owner);

    /// <summary>Closes the clipboard, releasing the exclusive handle.</summary>
    void CloseClipboard();

    /// <summary>
    /// Reads the CF_UNICODETEXT (13) format from the currently-open clipboard.
    /// Returns <c>null</c> if no text is available.
    /// </summary>
    string? GetText();

    /// <summary>
    /// Places <paramref name="text"/> onto the currently-open clipboard as CF_UNICODETEXT.
    /// The implementation must call <c>EmptyClipboard</c> before setting data.
    /// </summary>
    void SetText(string text);
}
