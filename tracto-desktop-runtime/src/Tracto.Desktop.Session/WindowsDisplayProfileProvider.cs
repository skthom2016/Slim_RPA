using System.Runtime.InteropServices;

namespace Tracto.Desktop.Session;

/// <summary>
/// Retrieves the primary monitor's display profile via Win32 GetSystemMetrics and GetDeviceCaps.
/// </summary>
public sealed class WindowsDisplayProfileProvider : IDisplayProfileProvider
{
    private static class NativeMethods
    {
        internal const int SM_CXSCREEN  = 0;
        internal const int SM_CYSCREEN  = 1;
        internal const int LOGPIXELSX   = 88;
        internal const int LOGPIXELSY   = 90;

        [DllImport("user32.dll")]
        internal static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        internal static extern nint GetDC(nint hWnd);

        [DllImport("user32.dll")]
        internal static extern int ReleaseDC(nint hWnd, nint hDC);

        [DllImport("gdi32.dll")]
        internal static extern int GetDeviceCaps(nint hDC, int nIndex);
    }

    public DisplayProfile GetCurrentProfile()
    {
        int widthPx  = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        int heightPx = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);

        var hDC = NativeMethods.GetDC(nint.Zero);
        int dpiX, dpiY;
        try
        {
            dpiX = NativeMethods.GetDeviceCaps(hDC, NativeMethods.LOGPIXELSX);
            dpiY = NativeMethods.GetDeviceCaps(hDC, NativeMethods.LOGPIXELSY);
        }
        finally
        {
            NativeMethods.ReleaseDC(nint.Zero, hDC);
        }

        return new DisplayProfile(widthPx, heightPx, dpiX, dpiY);
    }
}
