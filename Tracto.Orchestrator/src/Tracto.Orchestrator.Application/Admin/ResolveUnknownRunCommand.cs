#nullable enable
using Microsoft.Extensions.Logging;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Journal;
using Tracto.Orchestrator.Domain.Runs;

namespace Tracto.Orchestrator.Application.Admin;

// ── Command ────────────────────────────────────────────────────────────────────

/// <summary>
/// Operator-driven resolution of a run that is stuck in UNKNOWN state (OC-13).
/// POST /admin/jobs/{runId}/resolve
///
/// Resolution transitions:
///   UNKNOWN → SUCCEEDED : admin confirms the automation completed correctly.
///   UNKNOWN → FAILED    : admin confirms the automation failed.
///   UNKNOWN → CANCELLED : admin cancels the run explicitly.
///
/// Domain invariant 2: only UNKNOWN runs may be resolved via this path.
/// Domain invariant 3: AttemptId must match; stale AttemptIds → 409.
/// Domain invariant 5: all writes atomic in one EF Core transaction.
/// </summary>
public sealed record ResolveUnknownRunCommand(
    Guid         RunId,
    Guid         AttemptId,
    /// <summary>Target state — must be SUCCEEDED, FAILED, or CANCELLED.</summary>
    RunState     Resolution,
    string?      ResolutionNote = null);

// ── Result ─────────────────────────────────────────────────────────────────────

public sealed record ResolveUnknownRunResponse(
    Guid   RunId,
    string ResolvedState,
    Guid   AttemptId);

// ── Handler ────────────────────────────────────────────────────────────────────

/// <summary>Handles <see cref="ResolveUnknownRunCommand"/> for OC-13.</summary>
public sealed class ResolveUnknownRunCommandHandler
{
    private static readonly RunState[] AllowedResolutions =
    [
        RunState.Succeeded,
        RunState.Failed,
        RunState.Cancelled,
    ];

    private readonly IRunRepository         _runRepo;
    private readonly IFinalResultRepository _finalResultRepo;
    private readonly IJournalRepository     _journalRepo;
    private readonly IUnitOfWork            _unitOfWork;
    private readonly IClock                 _clock;
    private readonly ILogger<ResolveUnknownRunCommandHandler> _logger;

    public ResolveUnknownRunCommandHandler(
        IRunRepository         runRepo,
        IFinalResultRepository finalResultRepo,
        IJournalRepository     journalRepo,
        IUnitOfWork            unitOfWork,
        IClock                 clock,
        ILogger<ResolveUnknownRunCommandHandler> logger)
    {
        _runRepo         = runRepo;
        _finalResultRepo = finalResultRepo;
        _journalRepo     = journalRepo;
        _unitOfWork      = unitOfWork;
        _clock           = clock;
        _logger          = logger;
    }

    public async Task<Result<ResolveUnknownRunResponse, Error>> HandleAsync(
        ResolveUnknownRunCommand command,
        CancellationToken        cancellationToken = default)
    {
        // ── Validate resolution target ─────────────────────────────────────────
        if (!Array.Exists(AllowedResolutions, s => s == command.Resolution))
            return Result<ResolveUnknownRunResponse, Error>.Fail(
                Error.Validation(
                    $"Resolution '{command.Resolution}' is not permitted. " +
                    "Allowed values: SUCCEEDED, FAILED, CANCELLED."));

        // ── Load run ───────────────────────────────────────────────────────────
        var run = await _runRepo.GetByIdAsync(command.RunId, cancellationToken);
        if (run is null)
            return Result<ResolveUnknownRunResponse, Error>.Fail(
                Error.NotFound($"Run '{command.RunId}' not found."));

        // ── Source state must be UNKNOWN ───────────────────────────────────────
        if (run.State != RunState.Unknown)
            return Result<ResolveUnknownRunResponse, Error>.Fail(
                Error.Conflict(
                    "WRONG_STATE",
                    $"Run '{command.RunId}' is in state '{run.State}'; " +
                    "only UNKNOWN runs may be resolved via this endpoint."));

        // ── AttemptId fence — domain invariant 3 ──────────────────────────────
        if (command.AttemptId != run.AttemptId)
        {
            _logger.LogWarning(
                "ResolveUnknownRun: AttemptId mismatch for run {RunId}. " +
                "Expected {Expected}, received {Received}.",
                command.RunId, run.AttemptId, command.AttemptId);

            return Result<ResolveUnknownRunResponse, Error>.Fail(
                Error.Conflict(
                    "WRONG_ATTEMPT",
                    $"AttemptId mismatch. Expected {run.AttemptId}, received {command.AttemptId}."));
        }

        // ── Atomic write — domain invariant 5 ─────────────────────────────────
        await _unitOfWork.BeginAsync(cancellationToken);
        var committed = false;
        try
        {
            var now = _clock.UtcNow;
            ApplyResolution(run, command.Resolution, now);
            _runRepo.Update(run);

            var note     = command.ResolutionNote ?? $"Admin resolved to {command.Resolution}.";
            var eventType = command.Resolution switch
            {
                RunState.Succeeded => "RUN_ADMIN_RESOLVED_SUCCEEDED",
                RunState.Failed    => "RUN_ADMIN_RESOLVED_FAILED",
                RunState.Cancelled => "RUN_ADMIN_RESOLVED_CANCELLED",
                _                  => "RUN_ADMIN_RESOLVED",
            };

            await _journalRepo.AppendAsync(
                new JournalEntry(
                    runId:      command.RunId,
                    attemptId:  command.AttemptId,
                    eventType:  eventType,
                    occurredAt: now,
                    payload:    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        resolution = command.Resolution.ToString().ToUpperInvariant(),
                        note,
                    })),
                cancellationToken);

            // Write FinalResultRecord only when one does not already exist.
            // UNKNOWN runs always have a FinalResultRecord written when they enter UNKNOWN state
            // (ReconcileRunCommand / CommitFinalResultCommand UNKNOWN path). The DB has a unique
            // index on RunId (IX_FinalResultRecords_RunId), so unconditionally calling AddAsync
            // here would raise a constraint violation — CRITICAL bug fixed by this guard.
            var existingFinalResult = await _finalResultRepo.FindByRunIdAsync(command.RunId, cancellationToken);
            if (existingFinalResult is null)
            {
                var finalResult = new FinalResultRecord(
                    runId:           command.RunId,
                    attemptId:       command.AttemptId,
                    terminalState:   command.Resolution,
                    errorCategory:   command.Resolution == RunState.Failed ? "ADMIN_RESOLUTION" : null,
                    errorMessage:    note,
                    businessOutcome: null,
                    evidenceRefs:    [],
                    businessJobId:   run.BusinessJobId,
                    completedAt:     now,
                    unknownReason:   null);

                await _finalResultRepo.AddAsync(finalResult, cancellationToken);
            }

            await _unitOfWork.CommitAsync(cancellationToken);
            committed = true;

            _logger.LogInformation(
                "ResolveUnknownRun: run {RunId} resolved to {State} by admin.",
                command.RunId, command.Resolution);

            return Result<ResolveUnknownRunResponse, Error>.Ok(
                new ResolveUnknownRunResponse(
                    RunId:         command.RunId,
                    ResolvedState: command.Resolution.ToString().ToUpperInvariant(),
                    AttemptId:     command.AttemptId));
        }
        finally
        {
            if (!committed)
                await _unitOfWork.RollbackAsync();
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Delegates the UNKNOWN → terminal state transition to the domain entity.
    /// <see cref="RunRecord.AdminResolve"/> is the only transition that accepts UNKNOWN
    /// as a source state; it also clears <c>NeedsAttention</c> and <c>UnknownReason</c>.
    /// </summary>
    private static void ApplyResolution(RunRecord run, RunState resolution, DateTimeOffset now)
        => run.AdminResolve(resolution, now);
}
