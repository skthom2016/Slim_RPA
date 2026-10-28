namespace Tracto.Desktop.AppLifecycle;

/// <summary>
/// Snapshot of a top-level window's identity and ownership at enumeration time.
/// </summary>
/// <param name="Handle">Win32 HWND.</param>
/// <param name="Title">Window title text (may be empty).</param>
/// <param name="ClassName">Win32 window class name.</param>
/// <param name="ProcessId">OS PID of the owning process.</param>
public sealed record WindowInfo(IntPtr Handle, string Title, string ClassName, int ProcessId);
