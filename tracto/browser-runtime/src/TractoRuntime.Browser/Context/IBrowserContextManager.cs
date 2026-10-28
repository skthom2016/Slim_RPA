using Microsoft.Playwright;
using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Context;

public interface IBrowserContextManager : IAsyncDisposable
{
    /// <summary>
    /// Registers a signed vault reference for an approved storageState profile for a specific run.
    /// Must be called before <see cref="AcquireContextAsync"/> when the validated artifact declares
    /// profile reuse.  An unregistered run always receives a completely fresh context (AC#3).
    /// </summary>
    void RegisterApprovedProfile(string runId, string signedRef);

    Task<IBrowserContext> AcquireContextAsync(string runId, BrowserMode mode, CancellationToken ct);
    Task ReleaseContextAsync(string runId, CancellationToken ct);
}
