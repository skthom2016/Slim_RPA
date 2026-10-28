using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Continuation;

/// <summary>
/// Result returned by <see cref="IAsyncContinuationHandler.ProcessCallbackAsync"/>.
///
/// A continuation run is created ONLY when <see cref="Accepted"/> is <c>true</c>.
/// When <see cref="ServiceOutcome.UNKNOWN_OUTCOME"/> is received the attempt terminates as
/// UNKNOWN (terminal run state per CEC-016); <see cref="Accepted"/> will be <c>false</c>
/// and <see cref="TerminateAsUnknown"/> will be <c>true</c>.
/// </summary>
public sealed record ContinuationResult
{
    /// <summary><c>true</c> when the callback was eligible and a continuation run was created.</summary>
    public required bool Accepted { get; init; }

    /// <summary>Human-readable reason when <see cref="Accepted"/> is <c>false</c>.</summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Identifier of the new continuation run, populated only when <see cref="Accepted"/> is <c>true</c>.
    /// </summary>
    public string? ContinuationRunId { get; init; }

    /// <summary>The callback's service outcome, populated when accepted.</summary>
    public ServiceOutcome? ServiceOutcome { get; init; }

    /// <summary>
    /// When <c>true</c>, the run engine must terminate the current attempt as
    /// <see cref="RunState.UNKNOWN"/> — caused by a callback carrying
    /// <see cref="Contracts.ServiceOutcome.UNKNOWN_OUTCOME"/>.
    /// </summary>
    public bool TerminateAsUnknown { get; init; }

    // -------------------------------------------------------------------------
    // Factory helpers — used by AsyncContinuationHandler
    // -------------------------------------------------------------------------

    /// <summary>Creates a successful continuation result with the given run ID.</summary>
    public static ContinuationResult Created(string continuationRunId) => new()
    {
        Accepted          = true,
        ContinuationRunId = continuationRunId
    };

    /// <summary>Creates a rejected result with the supplied reason and optional UNKNOWN terminal state.</summary>
    public static ContinuationResult Rejected(
        string reason,
        RunState terminalState = RunState.UNKNOWN) => new()
    {
        Accepted           = false,
        Reason             = reason,
        TerminateAsUnknown = terminalState == RunState.UNKNOWN
    };
}
