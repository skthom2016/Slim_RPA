namespace Tracto.Designer.Core.Browser;

/// <param name="BrowserType">Browser to launch or attach to.</param>
/// <param name="DebugPort">DevTools port.</param>
/// <param name="TargetUrl">When attaching, the tab whose URL contains this; otherwise the first page tab.</param>
/// <param name="StartUrl">Page opened when the recorder launches the browser.</param>
/// <param name="LaunchBrowser">Launch a fresh browser with a clean profile; when false, attach to one already running with the debug port open.</param>
/// <param name="UserDataDir">
/// Browser profile folder to record in. Null records in the recorder's own throwaway folder,
/// with nothing signed in. Pointing it at the signed-in profile records what the segment will
/// actually see at run time, so a site already signed in stays signed in while recording.
/// </param>
public sealed record BrowserCaptureOptions(
    BrowserType BrowserType = BrowserType.Chromium,
    int DebugPort = 9222,
    string? TargetUrl = null,
    string? StartUrl = null,
    bool LaunchBrowser = true,
    string? UserDataDir = null);
