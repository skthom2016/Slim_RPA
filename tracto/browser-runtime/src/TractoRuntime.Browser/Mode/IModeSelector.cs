using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Mode;

/// <summary>
/// Selects and validates the browser execution mode (headless or headed) for a given run.
/// BRW-US-002 — Headless and Headed Execution Mode Selection.
/// </summary>
public interface IModeSelector
{
    /// <summary>
    /// Reads the mode declared in <paramref name="script"/> and produces a minimal
    /// <see cref="ExecutionModeConfig"/>. Session ID is not yet resolved — call
    /// <see cref="ValidateModeAsync"/> to resolve it and confirm prerequisites.
    /// </summary>
    ExecutionModeConfig SelectMode(BrowserScript script);

    /// <summary>
    /// Validates that the prerequisites for <paramref name="config"/> are satisfied
    /// and returns an enriched config with the resolved <c>InteractiveSessionId</c>
    /// (headed mode only).
    /// Throws <see cref="ModeValidationException"/> when prerequisites are not met:
    /// headed mode with no active Windows session yields
    /// <see cref="ErrorCategory.SESSION_UNAVAILABLE"/>.
    /// BRW-US-002 AC#1 (headless), AC#2 (headed requires active session).
    /// </summary>
    Task<ExecutionModeConfig> ValidateModeAsync(ExecutionModeConfig config, CancellationToken ct);
}
