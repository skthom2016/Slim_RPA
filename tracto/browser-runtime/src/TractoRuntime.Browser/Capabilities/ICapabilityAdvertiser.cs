using TractoRuntime.Browser.Mode;

namespace TractoRuntime.Browser.Capabilities;

/// <summary>
/// Builds the capability advertisement payload reported to the Orchestrator before the first step.
/// BRW-US-002 AC#4 — reports mode and interactiveSessionId (headed only).
/// BRW-006 — includes browser engine/version and capability digest.
/// </summary>
public interface ICapabilityAdvertiser
{
    /// <summary>
    /// Constructs a <see cref="WorkerCapabilityPayload"/> from the validated
    /// <paramref name="modeConfig"/> and the pinned runtime metadata.
    /// </summary>
    Task<WorkerCapabilityPayload> BuildPayloadAsync(
        string workerId,
        ExecutionModeConfig modeConfig,
        CancellationToken ct);
}
