#nullable enable
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Journal;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Domain.Telemetry;

namespace Tracto.Orchestrator.Application.Jobs;

// ── Lightweight journal entry DTO ──────────────────────────────────────────────

/// <summary>
/// Lightweight journal entry used by <see cref="ReconcileRunCommand"/>.
/// Carries only the fields needed for reconciliation decisions — the full domain
/// <see cref="JournalEntry"/> is not required (and the API path builds entries from
/// worker-supplied data, not from stored domain objects).
/// </summary>
public sealed record ReconciliationJournalEntry(
    string         EventType,
    Guid?          AttemptId,
    DateTimeOffset OccurredAt);

// ── Command ────────────────────────────────────────────────────────────────────

/// <summary>
/// Reconciles an automation run whose worker disconnected or whose lease was expired
/// by the heartbeat monitor (OC-05), so the orchestrator can determine the final outcome.
///
/// Called from two places:
///   1. ReconciliationService background job (every 15 s).
///   2. POST /jobs/{runId}/reconcile API endpoint (worker submits its own journal).
///
/// AttemptId fencing (domain invariant 3):
///   - Command AttemptId must match run.AttemptId -> 409 WRONG_ATTEMPT.
///   - Duplicate call when run is already terminal -> idempotent 200.
///
/// Guaranteed invariants:
///   - NEVER sets State = QUEUED or CREATED (no auto-replay).
///   - AttemptId is NOT incremented; original AttemptId is retained.
///   - All writes are atomic inside one EF Core transaction (domain invariant 5).
/// </summary>
public sealed record ReconcileRunCommand(
    Guid                         RunId,
    Guid                         AttemptId,
    ReconciliationJournalEntry[] JournalEntries);

// ── Result DTO ─────────────────────────────────────────────────────────────────

/// <summary>Value returned to the caller on successful reconciliation.</summary>
public sealed record ReconcileRunResponse(
    Guid   RunId,
    Guid   AttemptId,
    string ReconciledState,
    bool   WasAlreadyTerminal);

// ── Handler ────────────────────────────────────────────────────────────────────

/// <summary>
/// Handles ReconcileRunCommand — story OC-10.
///
/// Decision algorithm:
///   1. Load run. If not found -> 404.
///   2. AttemptId fence check -> 409 WRONG_ATTEMPT on mismatch.
///   3. Run already terminal -> idempotent 200, no write.
///   4. Inspect JournalEntries for SucceededEventType with matching AttemptId:
///      a. Found  -> SUCCEEDED path: advance to RUNNING if still LEASED, then Succeed.
///                   Write FinalResultRecord. Append RUN_RECONCILED_SUCCEEDED journal entry.
///      b. Not found -> UNKNOWN path: MarkUnknown("Reconciliation: uncertain journal").
///                   Write FinalResultRecord (UNKNOWN). Append RUN_RECONCILED_UNKNOWN.
///   5. Commit atomically (domain invariant 5).
///
/// Auto-replay invariant: NEVER sets State = QUEUED or CREATED.
/// Attempt-count invariant: AttemptId is NOT changed by reconcile.
/// </summary>
public sealed class ReconcileRunCommandHandler
{
    /// <summary>
    /// Journal event type written by CommitFinalResultCommandHandler when a run succeeds.
    /// Presence of this event (with matching AttemptId) in the supplied journal entries
    /// is treated as proof that the run finished successfully before the worker disconnected.
    /// </summary>
    public const string SucceededEventType = "RUN_SUCCEEDED";

    private readonly IRunRepository              _runRepo;
    private readonly IFinalResultRepository      _finalResultRepo;
    private readonly IJournalRepository          _journalRepo;
    private readonly IUnitOfWork                 _unitOfWork;
    private readonly IClock                      _clock;
    private readonly ITelemetryService           _telemetry;
    private readonly ILogger<ReconcileRunCommandHandler> _logger;

    public ReconcileRunCommandHandler(
        IRunRepository              runRepo,
        IFinalResultRepository      finalResultRepo,
        IJournalRepository          journalRepo,
        IUnitOfWork                 unitOfWork,
        IClock                      clock,
        ITelemetryService           telemetry,
        ILogger<ReconcileRunCommandHandler> logger)
    {
        _runRepo         = runRepo;
        _finalResultRepo = finalResultRepo;
        _journalRepo     = journalRepo;
        _unitOfWork      = unitOfWork;
        _clock           = clock;
        _telemetry       = telemetry;
        _logger          = logger;
    }

    public async Task<Result<ReconcileRunResponse, Error>> HandleAsync(
        ReconcileRunCommand command,
        CancellationToken   cancellationToken = default)
    {
        // ── 1. Load run ────────────────────────────────────────────────────────

        var run = await _runRepo.GetByIdAsync(command.RunId, cancellationToken);
        if (run is null)
            return Result<ReconcileRunResponse, Error>.Fail(
                Error.NotFound($"Run '{command.RunId}' not found."));

        // ── 2. AttemptId fence ─────────────────────────────────────────────────
        // Domain invariant 3: stale or foreign AttemptId -> 409.

        if (command.AttemptId != run.AttemptId)
        {
            _logger.LogWarning(
                "ReconcileRun: AttemptId mismatch for run {RunId}. " +
                "Expected {Expected}, received {Received}.",
                command.RunId, run.AttemptId, command.AttemptId);

            return Result<ReconcileRunResponse, Error>.Fail(
                Error.Conflict("WRONG_ATTEMPT",
                    $"AttemptId mismatch. Expected {run.AttemptId}, received {command.AttemptId}."));
        }

        // ── 3. Idempotent check ────────────────────────────────────────────────
        // Run already terminal -> return current state without writing.

        if (run.IsTerminal())
        {
            _logger.LogInformation(
                "ReconcileRun: run {RunId} already terminal ({State}). Returning idempotent 200.",
                command.RunId, run.State);

            return Result<ReconcileRunResponse, Error>.Ok(new ReconcileRunResponse(
                RunId:              command.RunId,
                AttemptId:          command.AttemptId,
                ReconciledState:    run.State.ToString().ToUpperInvariant(),
                WasAlreadyTerminal: true));
        }

        // ── 4. Decide outcome from supplied journal entries ────────────────────
        // A RUN_SUCCEEDED entry with matching AttemptId is proof the run finished
        // successfully before the disconnect.

        var isConfirmedSucceeded = command.JournalEntries
            .Any(e => e.EventType == SucceededEventType
                   && e.AttemptId == command.AttemptId);

        // ── 5. Atomic write ────────────────────────────────────────────────────

        await _unitOfWork.BeginAsync(cancellationToken);
        var committed = false;
        try
        {
            var now = _clock.UtcNow;

            if (isConfirmedSucceeded)
            {
                await ReconcileToSucceededAsync(run, command, now, cancellationToken);
            }
            else
            {
                await ReconcileToUnknownAsync(run, command, now, cancellationToken);
            }

            // Telemetry: emit the reconciled state event.
            Activity.Current?.SetTag("run.id", run.Id.ToString());
            Activity.Current?.SetTag("attempt.id", command.AttemptId.ToString());
            var telemetryEventType = isConfirmedSucceeded
                ? TelemetryEventType.RunSucceeded
                : TelemetryEventType.RunUnknown;
            await _telemetry.EmitAsync(new TelemetryEvent(
                id:        Guid.NewGuid(),
                runId:     run.Id,
                eventType: telemetryEventType,
                occurredAt: now,
                workerId:  run.WorkerId,
                attemptId: command.AttemptId,
                metadata:  JsonSerializer.Serialize(new
                {
                    source       = "reconciliation",
                    journalCount = command.JournalEntries.Length,
                    unknownReason = isConfirmedSucceeded ? null : "Reconciliation: uncertain journal",
                })),
                cancellationToken);

            await _unitOfWork.CommitAsync(cancellationToken);
            committed = true;

            _logger.LogInformation(
                "ReconcileRun: run {RunId} reconciled to {State}.",
                command.RunId, run.State);

            return Result<ReconcileRunResponse, Error>.Ok(new ReconcileRunResponse(
                RunId:              command.RunId,
                AttemptId:          command.AttemptId,
                ReconciledState:    run.State.ToString().ToUpperInvariant(),
                WasAlreadyTerminal: false));
        }
        finally
        {
            if (!committed)
                await _unitOfWork.RollbackAsync();
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private async Task ReconcileToSucceededAsync(
        RunRecord           run,
        ReconcileRunCommand command,
        DateTimeOffset      now,
        CancellationToken   cancellationToken)
    {
        // ForceSucceed transitions from ANY non-terminal state (LEASED, RUNNING, QUEUED, …)
        // to SUCCEEDED, validates the AttemptId fence, and does NOT issue a new AttemptId.
        // This is the correct path for reconciliation: the worker may have crashed between
        // any two stages and the orchestrator cannot require a specific predecessor state.
        run.ForceSucceed(command.AttemptId, now);
        _runRepo.Update(run);

        var finalResult = new FinalResultRecord(
            runId:           command.RunId,
            attemptId:       command.AttemptId,
            terminalState:   RunState.Succeeded,
            errorCategory:   null,
            errorMessage:    null,
            businessOutcome: null,
            evidenceRefs:    [],
            businessJobId:   run.BusinessJobId,
            completedAt:     now,
            unknownReason:   null);

        await _finalResultRepo.AddAsync(finalResult, cancellationToken);

        await _journalRepo.AppendAsync(
            new JournalEntry(
                runId:      command.RunId,
                attemptId:  command.AttemptId,
                eventType:  "RUN_RECONCILED_SUCCEEDED",
                occurredAt: now,
                payload:    BuildPayload("succeeded", command.JournalEntries.Length)),
            cancellationToken);

        _logger.LogInformation(
            "ReconcileRun: run {RunId} reconciled to SUCCEEDED " +
            "(journal had {Count} entries with RUN_SUCCEEDED).",
            command.RunId, command.JournalEntries.Length);
    }

    private async Task ReconcileToUnknownAsync(
        RunRecord           run,
        ReconcileRunCommand command,
        DateTimeOffset      now,
        CancellationToken   cancellationToken)
    {
        // Domain invariant 2: UNKNOWN requires NeedsAttention=true + non-null unknownReason.
        // NEVER auto-replay: MUST NOT set State=QUEUED or CREATED.
        const string unknownReason = "Reconciliation: uncertain journal";

        run.MarkUnknown(unknownReason, now);
        _runRepo.Update(run);

        var finalResult = new FinalResultRecord(
            runId:           command.RunId,
            attemptId:       command.AttemptId,
            terminalState:   RunState.Unknown,
            errorCategory:   "RECONCILIATION_UNKNOWN",
            errorMessage:    unknownReason,
            businessOutcome: null,
            evidenceRefs:    [],
            businessJobId:   run.BusinessJobId,
            completedAt:     now,
            unknownReason:   unknownReason);

        await _finalResultRepo.AddAsync(finalResult, cancellationToken);

        await _journalRepo.AppendAsync(
            new JournalEntry(
                runId:      command.RunId,
                attemptId:  command.AttemptId,
                eventType:  "RUN_RECONCILED_UNKNOWN",
                occurredAt: now,
                payload:    BuildPayload("uncertain", command.JournalEntries.Length)),
            cancellationToken);

        _logger.LogWarning(
            "ReconcileRun: run {RunId} reconciled to UNKNOWN " +
            "(journal had {Count} entries, no RUN_SUCCEEDED with matching AttemptId).",
            command.RunId, command.JournalEntries.Length);
    }

    private static string BuildPayload(string outcome, int journalCount)
        => System.Text.Json.JsonSerializer.Serialize(new
        {
            source       = "reconciliation",
            outcome,
            journalCount,
        });
}
