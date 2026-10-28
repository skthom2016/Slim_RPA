namespace Tracto.Designer.Core.Browser;

public sealed record BrowserCaptureOptions(
    BrowserType BrowserType = BrowserType.Chrome,
    int DebugPort = 9222,
    string? TargetUrl = null);
