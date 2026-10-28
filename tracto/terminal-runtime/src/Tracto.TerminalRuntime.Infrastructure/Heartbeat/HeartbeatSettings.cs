namespace Tracto.TerminalRuntime.Infrastructure.Heartbeat;

/// <summary>
/// Configuration for the HTTP heartbeat sender (TER-US-018).
/// Bind from appsettings.json under the section <c>"TerminalRuntime:Heartbeat"</c>.
/// </summary>
public sealed class HeartbeatSettings
{
    /// <summary>
    /// Base URL of the Orchestrator control plane — no trailing slash.
    /// Example: <c>"https://orchestrator.internal"</c>.
    ///
    /// Must NOT contain credential material (CEC invariant #4).
    /// Authentication is handled by the underlying <see cref="System.Net.Http.HttpClient"/>
    /// pipeline (e.g. via a <c>DelegatingHandler</c>), not embedded here.
    /// </summary>
    public string OrchestratorBaseUrl { get; set; } = string.Empty;

    /// <summary>Per-request HTTP timeout in seconds. Default: 10.</summary>
    public int TimeoutSeconds { get; set; } = 10;
}
