using Tracto.TerminalRuntime.Application.Common;

namespace Tracto.TerminalRuntime.Application.Service;

/// <summary>
/// Maps a <see cref="ServiceInvokeResult"/> to the appropriate CEC v1.0.1 run state
/// and records the transition in the run journal.
///
/// Each of the five service outcomes must be handled exactly once:
/// <list type="bullet">
///   <item>SUCCEEDED         → run state SUCCEEDED</item>
///   <item>FAILED            → run state FAILED</item>
///   <item>BUSINESS_REJECTION → run state SUCCEEDED; businessOutcome="REJECTED"; no retry</item>
///   <item>UNKNOWN_OUTCOME   → attempt terminated as UNKNOWN; needsAttention=true; no continuation</item>
///   <item>ASYNC_ACKNOWLEDGED → run state HANDED_OFF; worker released immediately</item>
/// </list>
///
/// The handler branches only on the <c>ServiceOutcome</c> category or an approved
/// <c>actionCode</c> — never on domain fields (CEC v1.0.1 invariant #6).
/// </summary>
public interface IServiceOutcomeHandler
{
    /// <summary>
    /// Handles the service-invoke result and records the resulting run-state
    /// transition in the journal.
    /// </summary>
    Task<Result<HandleServiceOutcomeResult, Error>> HandleAsync(
        HandleServiceOutcomeCommand command,
        CancellationToken ct = default);
}
