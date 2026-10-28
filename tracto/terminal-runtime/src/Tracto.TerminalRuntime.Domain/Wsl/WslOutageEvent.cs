namespace Tracto.TerminalRuntime.Domain.Wsl;

/// <summary>
/// Immutable record of a WSL distro outage detected during or before command execution.
/// Persisted to the run journal so that the Orchestrator can reconcile the attempt
/// without launching a duplicate command (TER-US-017 AC-3).
/// </summary>
/// <param name="OccurredAt">UTC instant at which the outage was detected.</param>
/// <param name="WasSideEffectCommitted">
///   True  → at least one effectful side-effect had been committed before the distro disappeared.
///   False → the distro disappeared before any side-effect was committed; the run is FAILED.
///   null  → the outcome could not be determined; the run is UNKNOWN.
/// </param>
public sealed record WslOutageEvent(
    DateTimeOffset OccurredAt,
    bool? WasSideEffectCommitted);
