namespace Tracto.Desktop.Session;

/// <summary>Provides the current display profile for environment validation.</summary>
public interface IDisplayProfileProvider
{
    /// <summary>Returns the display profile for the primary monitor.</summary>
    DisplayProfile GetCurrentProfile();
}
