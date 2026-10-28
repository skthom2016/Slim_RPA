using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Mode;

/// <summary>
/// Thrown when the prerequisites for the requested browser execution mode are not met.
/// The <see cref="ErrorCategory"/> drives the CEC error envelope reported to the Orchestrator.
/// BRW-US-002 AC#2 — headed mode with no active Windows session yields SESSION_UNAVAILABLE.
/// </summary>
public sealed class ModeValidationException(
    BrowserMode mode,
    string message,
    ErrorCategory errorCategory)
    : Exception(message)
{
    public BrowserMode Mode { get; } = mode;
    public ErrorCategory ErrorCategory { get; } = errorCategory;
}
