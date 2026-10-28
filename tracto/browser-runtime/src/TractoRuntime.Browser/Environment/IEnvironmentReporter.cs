using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Viewport;

namespace TractoRuntime.Browser.Environment;

/// <summary>
/// Collects and exposes browser environment metadata before the first step (BRW-006 / BRW-US-006).
/// <para>
/// Implementations must ensure that no credential, session token, cookie value, or secret
/// appears in either the returned <see cref="EnvironmentMetadata"/> or the evidence dictionary.
/// TLS and proxy configuration are carried as opaque reference identifiers only.
/// </para>
/// </summary>
public interface IEnvironmentReporter
{
    /// <summary>
    /// Collects a complete environment snapshot for the given run.
    /// Must be called before any browser step is executed.
    /// </summary>
    /// <param name="runId">Identifier of the run that owns this snapshot.</param>
    /// <param name="browserEngine">Playwright engine name ("chromium", "firefox", or "webkit").</param>
    /// <param name="browserVersion">Version string from the launched browser binary.</param>
    /// <param name="mode">Execution mode (Headless or Headed) declared in the deployment policy.</param>
    /// <param name="viewport">Validated viewport settings for this run.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A populated <see cref="EnvironmentMetadata"/> record.</returns>
    Task<EnvironmentMetadata> CollectAsync(
        string runId,
        string browserEngine,
        string browserVersion,
        BrowserMode mode,
        ViewportSettings viewport,
        CancellationToken ct);

    /// <summary>
    /// Builds a sanitized evidence dictionary from the collected metadata.
    /// The result is suitable for inclusion in diagnostic artifacts, traces, and
    /// Orchestrator run results.  No secret or credential may appear in the output.
    /// </summary>
    IReadOnlyDictionary<string, object?> BuildSanitizedEvidence(EnvironmentMetadata metadata);
}
