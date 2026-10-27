using System.Runtime.InteropServices;
using System.Text;

namespace Tracto.Desktop.Session;

/// <summary>
/// Determines the current session state by querying Win32 WTS and desktop APIs.
/// </summary>
public sealed class WindowsSessionStateProvider : ISessionStateProvider
{
    // ── P/Invoke declarations ────────────────────────────────────────────────

    private static class NativeMethods
    {
        internal const nint WTS_CURRENT_SERVER_HANDLE = 0;
        internal const uint WTS_CURRENT_SESSION = unchecked((uint)-1);

        internal enum WTS_INFO_CLASS
        {
            WTSConnectState = 8
        }

        internal enum WTS_CONNECTSTATE_CLASS
        {
            WTSActive = 0,
            WTSDisconnected = 4
        }

        internal const uint UOI_NAME = 2;

        [DllImport("wtsapi32.dll", SetLastError = true)]
        internal static extern bool WTSQuerySessionInformationW(
            nint hServer,
            uint sessionId,
            WTS_INFO_CLASS wtsInfoClass,
            out nint ppBuffer,
            out uint pBytesReturned);

        [DllImport("wtsapi32.dll")]
        internal static extern void WTSFreeMemory(nint pMemory);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern nint OpenInputDesktop(
            uint dwFlags,
            bool fInherit,
            uint dwDesiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool CloseDesktop(nint hDesktop);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern bool GetUserObjectInformationW(
            nint hObj,
            int nIndex,
            [Out] char[]? pvInfo,
            uint nLength,
            out uint lpnLengthNeeded);

        // READ_CONTROL | DESKTOP_READOBJECTS | DESKTOP_ENUMERATE
        internal const uint DESKTOP_READOBJECTS = 0x0001;
        internal const uint DESKTOP_ENUMERATE    = 0x0040;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public SessionState GetCurrentState()
    {
        // 1. Check whether a valid input desktop exists and its name is "Default".
        var desktopState = QueryDesktopName(out var desktopName);
        if (desktopState != SessionState.Interactive)
            return desktopState;

        // 2. Check WTS connection state for disconnect detection.
        var connectState = QueryWtsConnectState();
        if (connectState != SessionState.Interactive)
            return connectState;

        return SessionState.Interactive;
    }

    public string GetSessionDescription()
    {
        var state = GetCurrentState();
        return state switch
        {
            SessionState.Interactive    => "Session is interactively logged on with the Default input desktop.",
            SessionState.Locked         => "Session is locked or the input desktop is not the Default desktop.",
            SessionState.Disconnected   => "Session is disconnected (RDP or console disconnect).",
            SessionState.NonInteractive => "Process is running in a service or non-interactive context.",
            _                           => $"Unknown session state: {state}"
        };
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static SessionState QueryDesktopName(out string name)
    {
        name = string.Empty;
        var hDesk = NativeMethods.OpenInputDesktop(
            0,
            false,
            NativeMethods.DESKTOP_READOBJECTS | NativeMethods.DESKTOP_ENUMERATE);

        if (hDesk == nint.Zero)
        {
            // Cannot open the input desktop — likely running in a service/non-interactive context.
            return SessionState.NonInteractive;
        }

        try
        {
            // First call to get the required buffer size.
            NativeMethods.GetUserObjectInformationW(hDesk, (int)NativeMethods.UOI_NAME, null, 0, out var needed);
            var buf = new char[needed / sizeof(char) + 1];

            if (!NativeMethods.GetUserObjectInformationW(hDesk, (int)NativeMethods.UOI_NAME, buf, needed, out _))
                return SessionState.Locked;

            name = new string(buf).TrimEnd('\0');

            // The interactive default desktop is always named "Default".
            // The Winlogon (lock) screen desktop is named "Winlogon".
            // The UAC consent desktop is named "Default" on the Secure Desktop (handled by rejection of UAC prompts upstream).
            if (!string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase))
                return SessionState.Locked;

            return SessionState.Interactive;
        }
        finally
        {
            NativeMethods.CloseDesktop(hDesk);
        }
    }

    private static SessionState QueryWtsConnectState()
    {
        if (!NativeMethods.WTSQuerySessionInformationW(
                NativeMethods.WTS_CURRENT_SERVER_HANDLE,
                NativeMethods.WTS_CURRENT_SESSION,
                NativeMethods.WTS_INFO_CLASS.WTSConnectState,
                out var ppBuffer,
                out _))
        {
            // If WTS query fails in a non-RDP service context, treat as NonInteractive.
            return SessionState.NonInteractive;
        }

        try
        {
            var connectState = (NativeMethods.WTS_CONNECTSTATE_CLASS)Marshal.ReadInt32(ppBuffer);
            return connectState == NativeMethods.WTS_CONNECTSTATE_CLASS.WTSActive
                ? SessionState.Interactive
                : SessionState.Disconnected;
        }
        finally
        {
            NativeMethods.WTSFreeMemory(ppBuffer);
        }
    }
}
