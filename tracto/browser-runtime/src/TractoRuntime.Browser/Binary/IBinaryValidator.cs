namespace TractoRuntime.Browser.Binary;

public interface IBinaryValidator
{
    /// <summary>Validates the Playwright browser binary matches the pinned version and integrity hash.
    /// In isolated-host mode, binaries must be bundled — no runtime download permitted.</summary>
    Task ValidateAsync(string binaryPath, CancellationToken ct);
}
