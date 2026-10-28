using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Tracto.Desktop.Recovery;

/// <summary>
/// Production implementation of <see cref="ICrashDetector"/>.
/// <para>
/// Uses <c>Process.GetProcessById</c> to check <c>Process.HasExited</c> and
/// <c>EnumWindows</c> to detect Windows Error Reporting dialogs whose window class contains
/// <c>WerFault</c>.
/// </para>
/// <para>
/// For unit testing, pass custom delegates to the overloaded constructor
/// to avoid real Win32 calls.
/// </para>
/// </summary>
public sealed class CrashDetector : ICrashDetector
{
    private readonly Func<int, (bool hasExited, int? exitCode)> _inspectProcess;
    private readonly Func<bool> _hasWerDialog;

    /// <summary>
    /// Initialises a production crash detector that uses real Win32 APIs.
    /// </summary>
    public CrashDetector()
        : this(DefaultInspect, DefaultHasWerDialog)
    {
    }

    /// <summary>
    /// Initialises a crash detector with injectable dependencies (intended for testing).
    /// </summary>
    /// <param name="inspectProcess">
    /// Given a process ID, returns (hasExited, exitCode?).
    /// </param>
    /// <param name="hasWerDialog">
    /// Returns <see langword="true"/> if any top-level window with a class name containing
    /// "WerFault" exists on the desktop.
    /// </param>
    public CrashDetector(
        Func<int, (bool hasExited, int? exitCode)> inspectProcess,
        Func<bool> hasWerDialog)
    {
        _inspectProcess = inspectProcess;
        _hasWerDialog = hasWerDialog;
    }

    /// <inheritdoc />
    public CrashResult DetectCrash(int processId)
    {
        var (hasExited, exitCode) = _inspectProcess(processId);
        bool werDetected = hasExited && _hasWerDialog();
        return new CrashResult(IsCrashed: hasExited, ExitCode: exitCode, WerDialogDetected: werDetected);
    }

    // -------------------------------------------------------------------------
    // Private default implementations
    // -------------------------------------------------------------------------

    private static (bool hasExited, int? exitCode) DefaultInspect(int processId)
    {
        try
        {
            var p = Process.GetProcessById(processId);
            if (p.HasExited)
                return (true, p.ExitCode);
            return (false, null);
        }
        catch (ArgumentException)
        {
            // Process no longer exists — treat as crashed (exit code unavailable).
            return (true, null);
        }
        catch (InvalidOperationException)
        {
            // Process has already exited and the handle is closed.
            return (true, null);
        }
    }

    private static bool DefaultHasWerDialog()
    {
        bool found = false;

        bool EnumCallback(IntPtr hWnd, IntPtr lParam)
        {
            var sb = new StringBuilder(256);
            NativeMethods.GetClassName(hWnd, sb, sb.Capacity);
            if (sb.ToString().Contains("WerFault", StringComparison.OrdinalIgnoreCase))
            {
                found = true;
                return false; // stop enumeration
            }
            return true; // continue
        }

        NativeMethods.EnumWindows(EnumCallback, IntPtr.Zero);
        return found;
    }
}

internal static partial class NativeMethods
{
    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [LibraryImport("user32.dll")]
    internal static partial int GetWindowTextLength(IntPtr hWnd);
}
