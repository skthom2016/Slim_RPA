using System.Diagnostics;

namespace TractoRuntime.Browser.Mode;

/// <summary>
/// Production implementation of <see cref="IWindowsSessionInspector"/>.
/// Detects the current Windows interactive session using the process session ID.
/// On non-Windows platforms, always reports no interactive session, which causes
/// headed mode validation to fail (as intended — headed mode is Windows-only in v1).
/// BRW-US-002 AC#1 (headless requires no session), AC#2 (headed requires active session).
/// </summary>
public sealed class WindowsSessionInspector : IWindowsSessionInspector
{
    public bool HasInteractiveSession =>
        OperatingSystem.IsWindows() && System.Environment.UserInteractive;

    public Task<string?> GetActiveInteractiveSessionIdAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows() || !System.Environment.UserInteractive)
            return Task.FromResult<string?>(null);

        // The process session ID is the Windows logon session identifier.
        // A non-zero session ID on an interactive desktop confirms a live user session.
        using var proc = Process.GetCurrentProcess();
        int sid = proc.SessionId;
        return Task.FromResult<string?>(sid.ToString());
    }
}
