namespace Tracto.TerminalRuntime.Domain.Cleanup;

/// <summary>
/// Returned by <c>IServiceHandoffHandler</c> when a run successfully transitions
/// to <see cref="RunState.HandedOff"/>.
///
/// CEC v1.0.1 AC-3: the Terminal worker is released immediately upon HANDED_OFF;
/// continuation-run creation is deferred until an eligible callback arrives.
/// </summary>
public sealed record HandoffResult(
    /// <summary>Identifier of the run that was handed off.</summary>
    string RunId,

    /// <summary>Wall-clock time at which HANDED_OFF state was persisted.</summary>
    DateTimeOffset HandedOffAt,

    /// <summary>
    /// Reference to the statically-configured continuation mapping that will
    /// be used when an eligible callback arrives.
    /// Null when no continuation is configured for this run.
    /// </summary>
    string? ContinuationMappingRef);
