#nullable enable
using System.Runtime.InteropServices;
using System.Text;

namespace Tracto.Desktop.Actions;

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    internal static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // ── Keyboard helpers (Win32 keys unsupported by SendKeys) ─────────────────

    [DllImport("user32.dll")]
    internal static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);

    internal const byte VK_LWIN   = 0x5B;
    internal const byte VK_RWIN   = 0x5C;
    internal const uint KEYEVENTF_KEYUP = 0x0002;

    internal static void PressWinKey()
    {
        keybd_event(VK_LWIN, 0, 0, IntPtr.Zero);
        Thread.Sleep(50);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
        Thread.Sleep(50);
    }

    internal static void PressWinPlusKey(char letter)
    {
        keybd_event(VK_LWIN, 0, 0, IntPtr.Zero);
        Thread.Sleep(30);
        keybd_event((byte)char.ToUpper(letter), 0, 0, IntPtr.Zero);
        Thread.Sleep(30);
        keybd_event((byte)char.ToUpper(letter), 0, KEYEVENTF_KEYUP, IntPtr.Zero);
        Thread.Sleep(30);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
        Thread.Sleep(50);
    }

    // ── Mouse helpers ─────────────────────────────────────────────────────────

    [DllImport("user32.dll")]
    internal static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    internal static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, IntPtr dwExtraInfo);

    internal const uint MOUSEEVENTF_LEFTDOWN  = 0x0002;
    internal const uint MOUSEEVENTF_LEFTUP    = 0x0004;
    internal const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    internal const uint MOUSEEVENTF_RIGHTUP   = 0x0010;
    internal const uint MOUSEEVENTF_WHEEL     = 0x0800;

    internal static void LeftClick(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(50);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
        Thread.Sleep(30);
        mouse_event(MOUSEEVENTF_LEFTUP,   0, 0, 0, IntPtr.Zero);
    }

    internal static void RightClick(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(50);
        mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, IntPtr.Zero);
        Thread.Sleep(30);
        mouse_event(MOUSEEVENTF_RIGHTUP,   0, 0, 0, IntPtr.Zero);
    }

    internal static void WheelScroll(int x, int y, int delta)
    {
        SetCursorPos(x, y);
        Thread.Sleep(50);
        mouse_event(MOUSEEVENTF_WHEEL, 0, 0, (uint)delta, IntPtr.Zero);
    }

    // ── Window helpers ────────────────────────────────────────────────────────

    /// <summary>Returns the first visible top-level window whose title contains <paramref name="titleHint"/>.</summary>
    internal static IntPtr FindWindowByTitle(string titleHint)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            var buf = new StringBuilder(512);
            GetWindowText(hwnd, buf, buf.Capacity);
            if (buf.ToString().Contains(titleHint, StringComparison.OrdinalIgnoreCase))
            {
                found = hwnd;
                return false; // stop
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Escapes text for use with <c>SendKeys.SendWait</c>.</summary>
    internal static string EscapeForSendKeys(string text)
    {
        var sb = new StringBuilder(text.Length * 2);
        foreach (var ch in text)
        {
            if ("(){}[]^%~+".Contains(ch))
                sb.Append('{').Append(ch).Append('}');
            else
                sb.Append(ch);
        }
        return sb.ToString();
    }
}
