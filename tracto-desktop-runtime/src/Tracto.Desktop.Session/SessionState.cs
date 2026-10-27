namespace Tracto.Desktop.Session;

/// <summary>Represents the current interactive state of the Windows desktop session.</summary>
public enum SessionState
{
    /// <summary>The session is interactively logged on and the input desktop is the default desktop.</summary>
    Interactive,

    /// <summary>The session is locked (Winlogon/Secure desktop is active) or the input desktop is not "Default".</summary>
    Locked,

    /// <summary>The WTS connection state indicates the session is disconnected (RDP disconnected, etc.).</summary>
    Disconnected,

    /// <summary>The process is running in a service or background context with no interactive window station.</summary>
    NonInteractive
}
