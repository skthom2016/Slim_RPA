namespace Tracto.Desktop.Session;

/// <summary>Provides the current Windows session state for eligibility checks.</summary>
public interface ISessionStateProvider
{
    /// <summary>Returns the current session state.</summary>
    SessionState GetCurrentState();

    /// <summary>Returns a human-readable description of the current session state.</summary>
    string GetSessionDescription();
}
