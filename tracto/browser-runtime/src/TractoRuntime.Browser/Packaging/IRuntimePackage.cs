namespace TractoRuntime.Browser.Packaging;

public interface IRuntimePackage
{
    string RuntimeVersion { get; }
    string PlaywrightVersion { get; }
    IReadOnlyList<string> BundledBrowserChannels { get; }
    Task<bool> IsIsolatedDeploymentAsync(CancellationToken ct);
}
