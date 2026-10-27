using System.Text;

namespace Tracto.Desktop.Recovery;

/// <summary>
/// Detects foreground-window theft by comparing the current foreground window handle against
/// the expected automation target window.
/// </summary>
/// <remarks>
/// Pass a custom <c>getForegroundWindow</c> delegate in tests to avoid real Win32 calls.
/// </remarks>
public sealed class FocusTheftDetector
{
    private readonly Func<IntPtr> _getForegroundWindow;
    private readonly Func<IntPtr, string?> _getWindowTitle;

    /// <summary>
    /// Initialises a production focus-theft detector backed by <c>GetForegroundWindow</c>.
    /// </summary>
    public FocusTheftDetector()
        : this(NativeMethods.GetForegroundWindow, GetWindowTitleDefault)
    {
    }

    /// <summary>
    /// Initialises a focus-theft detector with injectable functions (intended for testing).
    /// </summary>
    /// <param name="getForegroundWindow">Returns the current foreground window handle.</param>
    /// <param name="getWindowTitle">Returns the sanitised title for a window handle, or <see langword="null"/>.</param>
    public FocusTheftDetector(Func<IntPtr> getForegroundWindow, Func<IntPtr, string?> getWindowTitle)
    {
        _getForegroundWindow = getForegroundWindow;
        _getWindowTitle = getWindowTitle;
    }

    /// <summary>
    /// Checks whether the foreground window is the <paramref name="expectedWindow"/>.
    /// </summary>
    /// <param name="expectedWindow">The window handle that should own the foreground.</param>
    /// <returns>
    /// A <see cref="FocusTheftResult"/> indicating whether focus has been stolen and, if so,
    /// the sanitised title of the stealing window.
    /// </returns>
    public FocusTheftResult Check(IntPtr expectedWindow)
    {
        var foreground = _getForegroundWindow();

        if (foreground == expectedWindow)
            return new FocusTheftResult(IsStolen: false, StealingWindowTitle: null);

        var title = _getWindowTitle(foreground);
        return new FocusTheftResult(IsStolen: true, StealingWindowTitle: title);
    }

    private static string? GetWindowTitleDefault(IntPtr hWnd)
    {
        int length = NativeMethods.GetWindowTextLength(hWnd);
        if (length <= 0)
            return null;

        var sb = new StringBuilder(length + 1);
        NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
