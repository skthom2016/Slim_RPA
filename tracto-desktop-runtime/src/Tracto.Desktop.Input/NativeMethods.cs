using System.Runtime.InteropServices;

namespace Tracto.Desktop.Input;

/// <summary>
/// Minimal P/Invoke surface for mouse input and screen metrics.
/// </summary>
internal static partial class NativeMethods
{
    internal const int SM_CXSCREEN = 0;
    internal const int SM_CYSCREEN = 1;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll")]
    internal static partial int GetSystemMetrics(int nIndex);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint SendInput(uint nInputs, [In] MouseInput[] pInputs, int cbSize);
}
