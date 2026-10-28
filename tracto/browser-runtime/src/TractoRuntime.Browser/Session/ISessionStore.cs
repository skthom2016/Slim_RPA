namespace TractoRuntime.Browser.Session;

public interface ISessionStore
{
    /// <summary>Saves Playwright storageState as a controlled secret reference.
    /// Value must never be embedded in scripts, traces, or Orchestrator payloads.</summary>
    Task<string> SaveStorageStateAsync(string runId, string storageStateJson, CancellationToken ct);
    Task<string?> LoadStorageStateAsync(string sessionRef, CancellationToken ct);
    Task DeleteAsync(string sessionRef, CancellationToken ct);
}
