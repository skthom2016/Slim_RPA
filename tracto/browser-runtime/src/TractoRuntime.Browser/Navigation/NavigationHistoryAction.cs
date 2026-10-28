namespace TractoRuntime.Browser.Navigation;

/// <summary>
/// The type of navigation operation supported by the Browser Runtime (BRW-US-005 AC#8, WEB-001).
/// </summary>
public enum NavigationHistoryAction
{
    /// <summary>Navigate to a specific absolute URL.</summary>
    Navigate,

    /// <summary>Reload the current page (hard reload).</summary>
    Reload,

    /// <summary>Navigate backward in the browser session history.</summary>
    Back,

    /// <summary>Navigate forward in the browser session history.</summary>
    Forward
}
