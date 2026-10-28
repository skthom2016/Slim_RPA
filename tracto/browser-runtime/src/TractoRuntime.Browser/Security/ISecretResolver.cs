namespace TractoRuntime.Browser.Security;

public interface ISecretResolver
{
    /// <summary>Resolves a secretRef to its plaintext value via Windows DPAPI or approved vault.
    /// Value must never be logged, traced, or placed in payloads.</summary>
    Task<string> ResolveAsync(string secretRef, CancellationToken ct);
}
