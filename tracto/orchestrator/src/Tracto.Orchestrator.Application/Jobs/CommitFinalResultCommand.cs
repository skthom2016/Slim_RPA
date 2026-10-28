#nullable enable
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Journal;
using Tracto.Orchestrator.Domain.Leases;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Domain.Telemetry;
using Tracto.Orchestrator.Domain.Workers;

namespace Tracto.Orchestrator.Application.Jobs;

// ── Command ────────────────────────────────────────────────────────────────────

/// <summary>
/// Commits the final result of an automation run (OC-06).
///
/// Terminal state transitions handled:
///   RUNNING -> SUCCEEDED | FAILED | HANDED_OFF | CANCELLED | TIMED_OUT | UNKNOWN
///
/// Key invariants:
///   - AttemptId mismatch -> 409 WRONG_ATTEMPT.
///   - Run already terminal -> idempotent 200 (no duplicate FinalResultRecord, no second slot release).
///   - UNKNOWN without UnknownReason -> 422 UNKNOWN_REASON_REQUIRED.
///   - Payload containing secret patterns -> 422 SENSITIVE_PAYLOAD + security event.
///   - Worker slot released exactly once (atomic commit).
/// </summary>
/// <param name="RunId">Primary key of the run to finalise.</param>
/// <param name="AttemptId">Must match the run's current AttemptId fence (invariant 3).</param>
/// <param name="TerminalState">The terminal RunState to transition to.</param>
/// <param name="ErrorCategory">Opaque error category (e.g. "SCRIPT_ERROR"). Null for non-error terminals.</param>
/// <param name="BusinessOutcome">Caller-supplied business label (e.g. "ORDER_FULFILLED").</param>
/// <param name="EvidenceRefs">References to evidence artifacts. Defaults to empty.</param>
/// <param name="BusinessJobId">Caller-supplied business-level job identifier.</param>
/// <param name="UnknownReason">Required when TerminalState == UNKNOWN (invariant 2).</param>
/// <param name="Payload">Optional opaque payload. Scanned for secret patterns before acceptance.</param>
public sealed record CommitFinalResultCommand(
    Guid      RunId,
    Guid      AttemptId,
    RunState  TerminalState,
    string?   ErrorCategory    = null,
    string?   BusinessOutcome  = null,
    string[]? EvidenceRefs     = null,
    string?   BusinessJobId    = null,
    string?   UnknownReason    = null,
    string?   Payload          = null);

// ── Result DTO ─────────────────────────────────────────────────────────────────

/// <summary>Value returned to the API on a successful commit.</summary>
public sealed record CommitFinalResultResponse(
    Guid   RunId,
    Guid   AttemptId,
    string TerminalState,
    bool   SlotReleased);

// ── Handler ────────────────────────────────────────────────────────────────────

/// <summary>
/// Handles <see cref="CommitFinalResultCommand"/> — story OC-06.
///
/// All writes are atomic inside a single EF Core transaction (domain invariant 5).
/// Journal entry is appended in the same transaction.
/// Worker slot is released exactly once: guarded by checking whether the run was
/// already terminal before this call entered.
/// </summary>
public sealed class CommitFinalResultCommandHandler
{
    // Patterns that indicate secret material in a payload.
    // Matching is intentionally conservative: false positives are safer than false negatives.
    // ["'\s]* after the keyword skips JSON quotes and whitespace before the separator (: or =),
    // enabling matches for both plain text (password=val) and JSON ("password":"val").
    private static readonly Regex[] SecretPatterns =
    [
        new(@"(?i)(password|passwd|pwd)[""'\s]*[:=][""'\s]*\S+",             RegexOptions.Compiled),
        new(@"(?i)(secret|api[_-]?key|apikey|token)[""'\s]*[:=][""'\s]*\S+", RegexOptions.Compiled),
        new(@"(?i)(auth[_-]?token|bearer)\s+[A-Za-z0-9\-._~+/]+=*",         RegexOptions.Compiled),
        new(@"(?i)(private[_-]?key|-----BEGIN)",                              RegexOptions.Compiled),
        new(@"(?i)aws[_-]?(secret[_-]?access[_-]?key|secret|access[_-]?key)", RegexOptions.Compiled),
    ];

    private readonly IRunRepository         _runRepo;
    private readonly IWorkerRepository      _workerRepo;
    private readonly IFinalResultRepository _finalResultRepo;
    private readonly IJournalRepository     _journalRepo;
    private readonly IUnitOfWork            _unitOfWork;
    private readonly IClock                 _clock;
    private readonly ITelemetryService      _telemetry;
    private readonly CreateContinuationRunCommandHandler _continuationHandler;
    private readonly ILeaseRepository       _leaseRepo;
    private readonly ILogger<CommitFinalResultCommandHandler> _logger;

    public CommitFinalResultCommandHandler(
        IRunRepository         runRepo,
        IWorkerRepository      workerRepo,
        IFinalResultRepository finalResultRepo,
        IJournalRepository     journalRepo,
        IUnitOfWork            unitOfWork,
        IClock                 clock,
        ITelemetryService      telemetry,
        CreateContinuationRunCommandHandler continuationHandler,
        ILeaseRepository       leaseRepo,
        ILogger<CommitFinalResultCommandHandler> logger)
    {
        _runRepo             = runRepo;
        _workerRepo          = workerRepo;
        _finalResultRepo     = finalResultRepo;
        _journalRepo         = journalRepo;
        _unitOfWork          = unitOfWork;
        _clock               = clock;
        _telemetry           = telemetry;
        _continuationHandler = continuationHandler;
        _leaseRepo           = leaseRepo;
        _logger              = logger;
    }

    public async Task<Result<CommitFinalResultResponse, Error>> HandleAsync(
        CommitFinalResultCommand command,
        CancellationToken        cancellationToken = default)
    {
        // ── 1. Basic validation ────────────────────────────────────────────────

        if (!IsTerminalState(command.TerminalState))
            return Result<CommitFinalResultResponse, Error>.Fail(
                Error.Validation($"'{command.TerminalState}' is not a legal terminal state."));

        if (command.TerminalState == RunState.Unknown &&
            string.IsNullOrWhiteSpace(command.UnknownReason))
        {
            return Result<CommitFinalResultResponse, Error>.Fail(
                Error.General("UNKNOWN_REASON_REQUIRED",
                    "UnknownReason is required when TerminalState is UNKNOWN."));
        }

        // ── 2. Secret scan ─────────────────────────────────────────────────────

        if (!string.IsNullOrEmpty(command.Payload) && ContainsSecret(command.Payload))
        {
            _logger.LogWarning(
                "[SECURITY] CommitFinalResult: sensitive payload detected for run {RunId}. " +
                "Rejecting with 422 SENSITIVE_PAYLOAD.", command.RunId);

            return Result<CommitFinalResultResponse, Error>.Fail(
                Error.General("SENSITIVE_PAYLOAD",
                    "The supplied payload appears to contain secret material and was rejected."));
        }

        // ── 3. Load run ────────────────────────────────────────────────────────

        var run = await _runRepo.GetByIdAsync(command.RunId, cancellationToken);
        if (run is null)
            return Result<CommitFinalResultResponse, Error>.Fail(
                Error.NotFound($"Run '{command.RunId}' not found."));

        // ── 4. AttemptId fence ─────────────────────────────────────────────────

        if (command.AttemptId != run.AttemptId)
        {
            _logger.LogWarning(
                "CommitFinalResult: AttemptId mismatch for run {RunId}. " +
                "Expected {Expected}, received {Received}.",
                command.RunId, run.AttemptId, command.AttemptId);

            return Result<CommitFinalResultResponse, Error>.Fail(
                Error.General("WRONG_ATTEMPT",
                    $"AttemptId mismatch. Expected {run.AttemptId}, received {command.AttemptId}."));
        }

        // ── 5. Idempotent check ────────────────────────────────────────────────

        if (run.IsTerminal())
        {
            _logger.LogInformation(
                "CommitFinalResult: run {RunId} is already terminal ({State}). " +
                "Returning idempotent 200.", command.RunId, run.State);

            return Result<CommitFinalResultResponse, Error>.Ok(
                new CommitFinalResultResponse(
                    RunId:         command.RunId,
                    AttemptId:     command.AttemptId,
                    TerminalState: run.State.ToString().ToUpperInvariant(),
                    SlotReleased:  false));
        }

        // ── 6. Atomic write ────────────────────────────────────────────────────

        await _unitOfWork.BeginAsync(cancellationToken);
        var committed = false;
        try
        {
            var now          = _clock.UtcNow;
            var evidenceRefs = command.EvidenceRefs ?? [];

            // Transition the run aggregate.
            TransitionRun(run, command, now);
            _runRepo.Update(run);

            // Persist the FinalResultRecord.
            var finalResult = new FinalResultRecord(
                runId:           command.RunId,
                attemptId:       command.AttemptId,
                terminalState:   command.TerminalState,
                errorCategory:   command.ErrorCategory,
                errorMessage:    null,          // not surfaced in the command for this story
                businessOutcome: command.BusinessOutcome,
                evidenceRefs:    evidenceRefs,
                businessJobId:   command.BusinessJobId,
                completedAt:     now,
                unknownReason:   command.UnknownReason);

            await _finalResultRepo.AddAsync(finalResult, cancellationToken);

            // Release the worker slot.
            var slotReleased = false;
            if (!string.IsNullOrWhiteSpace(run.WorkerId))
            {
                var worker = await _workerRepo.GetByIdAsync(run.WorkerId, cancellationToken);
                if (worker is not null)
                {
                    worker.MarkAvailable();
                    _workerRepo.Update(worker);
                    slotReleased = true;
                    _logger.LogInformation(
                        "CommitFinalResult: worker slot released for worker {WorkerId} " +
                        "(run {RunId}).", run.WorkerId, command.RunId);
                }
                else
                {
                    _logger.LogWarning(
                        "CommitFinalResult: worker {WorkerId} for run {RunId} not found; " +
                        "cannot release slot.", run.WorkerId, command.RunId);
                }
            }

            // Expire the active lease for this run (releases session exclusivity — invariant 6).
            // This is essential so that the worker's next PullJob call sees no active lease
            // and headed workers on the same interactiveSessionId can take new jobs.
            var activeLease = await _leaseRepo.FindActiveLeaseForRunAsync(command.RunId, cancellationToken);
            if (activeLease is not null)
            {
                activeLease.ForceExpire();
                _leaseRepo.Update(activeLease);
            }

            // Append journal entry in the same transaction (invariant 5).
            await _journalRepo.AppendAsync(
                new JournalEntry(
                    runId:      command.RunId,
                    attemptId:  command.AttemptId,
                    eventType:  BuildJournalEventType(command.TerminalState),
                    occurredAt: now,
                    payload:    BuildPayload(command, slotReleased)),
                cancellationToken);

            // Telemetry: emit the terminal-state transition event.
            Activity.Current?.SetTag("run.id", command.RunId.ToString());
            Activity.Current?.SetTag("attempt.id", command.AttemptId.ToString());
            string? telemetryMetadata = null;
            if (command.TerminalState == RunState.Succeeded)
            {
                // SUCCEEDED event requires stepCount + durationMs + businessOutcome.
                var journalEntries = await _journalRepo.GetByRunIdAsync(command.RunId, cancellationToken)
                    ?? Array.Empty<JournalEntry>();
                var durationMs     = (long)(now - run.CreatedAt).TotalMilliseconds;
                telemetryMetadata  = JsonSerializer.Serialize(new
                {
                    stepCount       = journalEntries.Count,
                    durationMs,
                    businessOutcome = command.BusinessOutcome,
                });
            }
            else if (command.TerminalState == RunState.Unknown)
            {
                telemetryMetadata = JsonSerializer.Serialize(new
                {
                    unknownReason = command.UnknownReason,
                });
            }
            else if (command.TerminalState == RunState.Failed)
            {
                telemetryMetadata = JsonSerializer.Serialize(new
                {
                    errorCategory = command.ErrorCategory,
                });
            }

            await _telemetry.EmitAsync(new TelemetryEvent(
                id:        Guid.NewGuid(),
                runId:     command.RunId,
                eventType: BuildTelemetryEventType(command.TerminalState),
                occurredAt: now,
                workerId:  run.WorkerId,
                attemptId: command.AttemptId,
                metadata:  telemetryMetadata),
                cancellationToken);

            await _unitOfWork.CommitAsync(cancellationToken);
            committed = true;

            _logger.LogInformation(
                "CommitFinalResult: run {RunId} committed to terminal state {State} " +
                "(slotReleased={SlotReleased}).",
                command.RunId, command.TerminalState, slotReleased);

            // ── 7. Auto-trigger continuation (OC-08) ──────────────────────────
            // After the final result is committed, check for and fire any matching
            // continuation mapping.  Errors are swallowed so the caller's 200 is preserved.
            try
            {
                var contCmd = new CreateContinuationRunCommand(
                    SourceRunId:  command.RunId,
                    TriggerState: command.TerminalState,
                    BusinessJobId: command.BusinessJobId);
                var contResult = await _continuationHandler.HandleAsync(contCmd, cancellationToken);
                if (contResult.IsFailure)
                {
                    _logger.LogWarning(
                        "CommitFinalResult: continuation trigger failed for run {RunId}: " +
                        "{Code} — {Message}.",
                        command.RunId, contResult.Error.Code, contResult.Error.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "CommitFinalResult: unexpected error during continuation trigger for run {RunId}.",
                    command.RunId);
            }

            return Result<CommitFinalResultResponse, Error>.Ok(
                new CommitFinalResultResponse(
                    RunId:         command.RunId,
                    AttemptId:     command.AttemptId,
                    TerminalState: command.TerminalState.ToString().ToUpperInvariant(),
                    SlotReleased:  slotReleased));
        }
        finally
        {
            if (!committed)
                await _unitOfWork.RollbackAsync();
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private static void TransitionRun(
        RunRecord                run,
        CommitFinalResultCommand cmd,
        DateTimeOffset           now)
    {
        switch (cmd.TerminalState)
        {
            case RunState.Succeeded:
                run.Succeed(cmd.AttemptId, now);
                break;
            case RunState.Failed:
                run.Fail(cmd.AttemptId, now);
                break;
            case RunState.HandedOff:
                run.HandOff(cmd.AttemptId, now);
                break;
            case RunState.TimedOut:
                run.MarkTimedOut(cmd.AttemptId, now);
                break;
            case RunState.Cancelled:
                // Cancel does not require an AttemptId fence — it is allowed from any non-terminal state.
                run.Cancel(now);
                break;
            case RunState.Unknown:
                // MarkUnknown does not take an AttemptId — it is enforced at the command level.
                run.MarkUnknown(cmd.UnknownReason!, now);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unhandled terminal state '{cmd.TerminalState}' in CommitFinalResultCommandHandler.");
        }
    }

    private static bool IsTerminalState(RunState state) =>
        state is RunState.Succeeded or RunState.Failed or RunState.HandedOff
               or RunState.Cancelled or RunState.TimedOut or RunState.Unknown;

    private static bool ContainsSecret(string payload)
    {
        foreach (var pattern in SecretPatterns)
        {
            if (pattern.IsMatch(payload))
                return true;
        }
        return false;
    }

    private static string BuildJournalEventType(RunState terminalState) =>
        terminalState switch
        {
            RunState.Succeeded => "RUN_SUCCEEDED",
            RunState.Failed    => "RUN_FAILED",
            RunState.HandedOff => "RUN_HANDED_OFF",
            RunState.Cancelled => "RUN_CANCELLED",
            RunState.TimedOut  => "RUN_TIMED_OUT",
            RunState.Unknown   => "RUN_UNKNOWN",
            _                  => "RUN_FINALISED"
        };

    private static string BuildTelemetryEventType(RunState terminalState) =>
        terminalState switch
        {
            RunState.Succeeded => TelemetryEventType.RunSucceeded,
            RunState.Failed    => TelemetryEventType.RunFailed,
            RunState.HandedOff => TelemetryEventType.RunHandedOff,
            RunState.Cancelled => TelemetryEventType.RunCancelled,
            RunState.TimedOut  => TelemetryEventType.RunTimedOut,
            RunState.Unknown   => TelemetryEventType.RunUnknown,
            _                  => TelemetryEventType.RunUnknown
        };

    private static string BuildPayload(CommitFinalResultCommand cmd, bool slotReleased)
        => JsonSerializer.Serialize(new
        {
            terminalState   = cmd.TerminalState.ToString(),
            errorCategory   = cmd.ErrorCategory,
            businessOutcome = cmd.BusinessOutcome,
            businessJobId   = cmd.BusinessJobId,
            unknownReason   = cmd.UnknownReason,
            evidenceCount   = (cmd.EvidenceRefs?.Length ?? 0),
            slotReleased
        });
}
