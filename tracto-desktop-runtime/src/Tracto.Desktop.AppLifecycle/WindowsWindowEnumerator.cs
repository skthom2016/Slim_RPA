using System.Runtime.InteropServices;
using System.Text;

namespace Tracto.Desktop.AppLifecycle;

/// <summary>
/// Production implementation of <see cref="IWindowEnumerator"/> that uses Win32 P/Invoke
/// (EnumWindows, GetWindowText, GetClassName, IsWindowVisible, GetWindowThreadProcessId).
/// </summary>
public sealed class WindowsWindowEnumerator : IWindowEnumerator
{
    // ── P/Invoke declarations ────────────────────────────────────────────────

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    private const uint GW_HWNDPREV = 3u; // window above in Z-order

    // ── IWindowEnumerator ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public IReadOnlyList<WindowInfo> EnumerateTopLevel()
    {
        var results = new List<WindowInfo>();

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;

            var info = BuildWindowInfo(hWnd);
            results.Add(info);
            return true; // continue
        }, IntPtr.Zero);

        return results;
    }

    /// <inheritdoc/>
    public IReadOnlyList<WindowInfo> FindByTitle(string titleFragment, int processId)
    {
        ArgumentNullException.ThrowIfNull(titleFragment);

        var results = new List<WindowInfo>();

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;

            var info = BuildWindowInfo(hWnd);

            if (info.ProcessId == processId &&
                info.Title.Contains(titleFragment, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(info);
            }

            return true;
        }, IntPtr.Zero);

        return results;
    }

    /// <inheritdoc/>
    public bool HasUnexpectedModal(IntPtr targetHandle)
    {
        if (targetHandle == IntPtr.Zero) return false;

        GetWindowThreadProcessId(targetHandle, out uint targetPid);

        // Walk Z-order upward from the target window. Any visible window above the target
        // that belongs to a different process is an unexpected modal.
        IntPtr above = GetWindow(targetHandle, GW_HWNDPREV);
        while (above != IntPtr.Zero)
        {
            if (IsWindowVisible(above))
            {
                GetWindowThreadProcessId(above, out uint abovePid);
                if (abovePid != targetPid) return true;
            }
            above = GetWindow(above, GW_HWNDPREV);
        }

        return false;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static WindowInfo BuildWindowInfo(IntPtr hWnd)
    {
        var titleSb = new StringBuilder(512);
        GetWindowText(hWnd, titleSb, titleSb.Capacity);

        var classSb = new StringBuilder(256);
        GetClassName(hWnd, classSb, classSb.Capacity);

        GetWindowThreadProcessId(hWnd, out uint pid);

        return new WindowInfo(hWnd, titleSb.ToString(), classSb.ToString(), (int)pid);
    }
}
