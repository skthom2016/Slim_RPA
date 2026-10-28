using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Mode;

/// <summary>
/// Production implementation of <see cref="IModeSelector"/>.
/// Resolves the execution mode from the script and validates Windows session
/// prerequisites for headed execution.
/// BRW-US-002 AC#1 (headless requires no session, AC#2 (headed requires active session).
/// </summary>
public sealed class ModeSelector(IWindowsSessionInspector sessionInspector) : IModeSelector
{
    public ExecutionModeConfig SelectMode(BrowserScript script)
    {
        // Mode is declared in the signed artifact — never derived from runtime state here.
        // Session ID resolution is deferred to ValidateModeAsync so that the caller
        // can choose when to perform the blocking OS inspection.
        BrowserMode mode = script.Mode;
        return new ExecutionModeConfig { Mode = mode, InteractiveSessionId = null };
    }

    public async Task<ExecutionModeConfig> ValidateModeAsync(ExecutionModeConfig config, CancellationToken ct)
    {
        if (config.Mode is BrowserMode.Headless)
        {
            // Headless: no Windows desktop session required — AC#1.
            // Return the config unchanged; no session ID to populate.
            return config;
        }

        // Headed: an authorized, active Windows interactive session is required — AC#2.
        string? sessionId = await sessionInspector.GetActiveInteractiveSessionIdAsync(ct);
        if (sessionId is null)
        {
            throw new ModeValidationException(
                BrowserMode.Headed,
                "Headed browser mode requires an authorized, active Windows interactive session, " +
                "but no session was detected. Ensure the host has an interactive logon before " +
                "scheduling headed browser jobs.",
                ErrorCategory.SESSION_UNAVAILABLE);
        }

        // Enrich the config with the resolved session ID for capability advertisement (AC#4).
        return config with { InteractiveSessionId = sessionId };
    }
}
