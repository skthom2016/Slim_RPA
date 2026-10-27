namespace Tracto.Desktop.AppLifecycle;

/// <summary>
/// Represents a running application process and its primary window.
/// </summary>
/// <param name="ProcessId">OS process identifier.</param>
/// <param name="ProcessName">Process image name (without extension), e.g. "notepad".</param>
/// <param name="MainWindowHandle">Win32 HWND of the process's main window; <see cref="IntPtr.Zero"/> if the process has no visible window.</param>
public sealed record AppHandle(int ProcessId, string ProcessName, IntPtr MainWindowHandle);
