namespace Tracto.TerminalRuntime.Domain.Outcomes;

/// <summary>
/// CEC v1.0.1 Orchestrator error categories surfaced on terminal run states.
/// Safe to include in Orchestrator payloads, journals, and audit records.
/// </summary>
public enum ErrorCategory
{
    /// <summary>
    /// The failure was caused by a transport-layer issue (network partition, pipe break,
    /// SSH disconnect) between the runtime and the target process.
    /// </summary>
    Transport,

    /// <summary>
    /// The failure originated inside the runtime or process itself
    /// (unhandled exception, OOM kill, hardware fault).
    /// </summary>
    Internal,

    /// <summary>
    /// The run exceeded its allotted time window.
    /// </summary>
    Timeout,

    /// <summary>
    /// A business-level rule rejected the command output
    /// (maps to Orchestrator serviceOutcome=BUSINESS_REJECTION).
    /// </summary>
    BusinessRejection
}
