using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain;
using Tracto.TerminalRuntime.Domain.Wsl;

namespace Tracto.TerminalRuntime.Application.Wsl;

// ---------------------------------------------------------------------------
// Command
// ---------------------------------------------------------------------------

/// <summary>
/// Raised by the Infrastructure monitoring layer when a WSL distro outage is
/// detected — either before command start (AC-1) or during execution (AC-2).
/// </summary>
/// <param name="RunId">The run that was affected.</param>
/// <param name="AttemptId">Attempt fence identifier (CEC v1.0.1 invariant #3).</param>
/// <param name="LeaseId">Lease fence identifier.</param>
/// <param name="DistroName">The WSL distro that is no longer running.</param>
/// <param name="OccurredBeforeCommandStart">
///   <see langword="true"/>  → outage detected before any effectful command was started (AC-1).
///   <see langword="false"/> → outage detected after the command began executing  (AC-2).
/// </param>
/// <param name="WasSideEffectCommitted">
///   Relevant only when <see cref="OccurredBeforeCommandStart"/> is
///   <see langword="false"/>.
///   <see langword="true"/>  → a side effect was definitely committed before the distro disappeared.
///   <see langword="false"/> → the distro disappeared before any side effect was committed.
///   <see langword="null"/>  → it cannot be determined whether a side effect was committed.
/// </param>
public sealed record HandleWslOutageCommand(
    string RunId,
    string AttemptId,
    string LeaseId,
    string DistroName,
    bool OccurredBeforeCommandStart,
    bool? WasSideEffectCommitted);

// ---------------------------------------------------------------------------
// Result
// ---------------------------------------------------------------------------

/// <summary>Returned when the outage was successfully classified and journaled.</summary>
public sealed record WslOutageHandled(
    string RunId,
    RunState NewState,
    WslOutageEvent OutageEvent,
    bool NeedsAttention,
    string? UnknownReason);

// ---------------------------------------------------------------------------
// Handler
// ---------------------------------------------------------------------------

/// <summary>
/// Classifies a WSL distro outage and transitions the run to the appropriate
/// terminal or error state:
/// <list type="bullet">
///   <item>Before command start → returns an ENVIRONMENT error (AC-1). No state change.</item>
///   <item>During execution, side effect not committed → <see cref="RunState.Failed"/> (AC-2).</item>
///   <item>During execution, side effect committed or unknown → <see cref="RunState.Unknown"/> (AC-2).</item>
/// </list>
/// The outage event is always persisted to the journal so the Orchestrator can
/// reconcile the attempt without launching a duplicate (AC-3).
///
/// No process-launch or network logic lives here (CEC invariant #5).
/// </summary>
public sealed class HandleWslOutageHandler(IRunJournal journal, IClock clock)
{
    public async Task<Result<WslOutageHandled, Error>> HandleAsync(
        HandleWslOutageCommand command,
        CancellationToken ct = default)
    {
        // AC-1: outage detected before any effectful command started.
        if (command.OccurredBeforeCommandStart)
            return Result<WslOutageHandled, Error>.Fail(
                Error.Environment(
                    "Wsl.DistroUnavailable",
                    $"WSL distro '{command.DistroName}' is not running. " +
                    "Cannot start effectful command.",
                    "terminal.wslUnavailable"));

        // AC-2: outage during execution — classify based on side-effect knowledge.
        // WasSideEffectCommitted = false  → no side effect: FAILED (clean failure).
        // WasSideEffectCommitted = true   → side effect committed, outcome unknown: UNKNOWN.
        // WasSideEffectCommitted = null   → cannot determine: UNKNOWN.
        RunState newState = command.WasSideEffectCommitted == false
            ? RunState.Failed
            : RunState.Unknown;

        // CEC v1.0.1 invariant #2: UNKNOWN requires a non-null unknownReason.
        string? unknownReason = newState == RunState.Unknown
            ? (command.WasSideEffectCommitted == true
                ? "WslOutage:SideEffectCommitted"
                : "WslOutage:SideEffectStatusIndeterminate")
            : null;

        var outageEvent = new WslOutageEvent(clock.UtcNow, command.WasSideEffectCommitted);

        // AC-3: write outage to journal so Orchestrator can reconcile.
        var recordResult = await journal.RecordWslOutageAsync(
            command.RunId, command.AttemptId, command.LeaseId, outageEvent, ct)
            .ConfigureAwait(false);
        if (recordResult.IsFailure)
            return Result<WslOutageHandled, Error>.Fail(recordResult.Error);

        // Transition run state (FAILED or UNKNOWN).
        // UNKNOWN transitions must set NeedsAttention=true and UnknownReason (invariant #2).
        Result<bool, Error> stateResult;
        if (newState == RunState.Unknown)
        {
            stateResult = await journal.RecordUnknownStateAsync(
                command.RunId, command.AttemptId, command.LeaseId, unknownReason!, ct)
                .ConfigureAwait(false);
        }
        else
        {
            stateResult = await journal.RecordStateAsync(
                command.RunId, command.AttemptId, command.LeaseId, newState, ct)
                .ConfigureAwait(false);
        }
        if (stateResult.IsFailure)
            return Result<WslOutageHandled, Error>.Fail(stateResult.Error);

        return Result<WslOutageHandled, Error>.Ok(
            new WslOutageHandled(command.RunId, newState, outageEvent,
                NeedsAttention: newState == RunState.Unknown,
                UnknownReason:  unknownReason));
    }
}
