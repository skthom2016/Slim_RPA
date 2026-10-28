namespace Tracto.TerminalRuntime.Domain.Outcomes;

/// <summary>
/// C3-approved reason codes for why a run entered the <see cref="RunState.Unknown"/> state.
///
/// CEC v1.0.1 invariant #2: UNKNOWN runs always carry a populated unknownReason.
/// These codes are safe to surface in Orchestrator payloads, journals, and audit records
/// — they describe the failure category, never secret values.
/// </summary>
public enum UnknownReason
{
    /// <summary>
    /// The transport connection to the process was lost after stdout evidence of a write was observed,
    /// but before the process exit code was received.
    /// Maps to Orchestrator errorCategory=TRANSPORT.
    /// </summary>
    TransportLostAfterWrite,

    /// <summary>
    /// The process was killed (via SIGKILL, TerminateProcess, or job-object termination) after stdout
    /// evidence of a write was observed, but before a clean exit code was received.
    /// Maps to Orchestrator errorCategory=INTERNAL.
    /// </summary>
    ProcessKilledAfterWrite,

    /// <summary>
    /// The run timed out while an irreversible side-effect was in flight.
    /// The write may or may not have completed on the target system.
    /// Maps to Orchestrator errorCategory=TIMEOUT.
    /// </summary>
    TimeoutDuringIrreversibleWrite,

    /// <summary>
    /// An unexpected internal error (e.g. runtime crash, unhandled exception) occurred during an
    /// irreversible command after write activity was observed on stdout.
    /// Maps to Orchestrator errorCategory=INTERNAL.
    /// </summary>
    InternalErrorAfterWrite
}
