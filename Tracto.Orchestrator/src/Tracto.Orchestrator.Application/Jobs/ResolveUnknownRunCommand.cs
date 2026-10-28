#nullable enable
using Microsoft.Extensions.Logging;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Journal;
using Tracto.Orchestrator.Domain.Runs;
using ResolutionRecord = Tracto.Orchestrator.Domain.Runs.ResolutionRecord;

namespace Tracto.Orchestrator.Application.Jobs;

// ── Command ───────────────────────────────────────────────────────────────────

/// <summary>
/// Resolves an UNKNOWN run (OC-12).
///
/// Two resolution types:
///   InvestigatedNoRerun — operator has investigated; no new run is created.
///   ApprovedNewRun      — operator approves a retry; a new run is created for the same script
///                         via <see cref="CreateJobCommand"/>; original stays UNKNOWN.
///
/// Auto-replay invariant: NO code path in this handler transitions the original
/// RunRecord from UNKNOWN to QUEUED. The original run's state is always preserved.
/// </summary>
public sealed record ResolveUnknownRunCommand(
    Guid   RunId,
    string ResolutionType,   // "INVESTIGATED_NO_RERUN" | "APPROVED_NEW_RUN"
    string Justification,
    string OperatorId);

// ── Result DTO ────────────────────────────────────────────────────────────────

/// <summary>
/// Returned on success.
/// NewRunId is non-null only for ApprovedNewRun resolutions.
/// </summary>
public sealed record ResolveUnknownRunResult(
    Guid  ResolutionId,
    Guid  RunId,
    Guid? NewRunId);

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// Handles <see cref="ResolveUnknownRunCommand"/>:
///   1. Validate justification — 400 when empty.
///   2. Load run — 404 when not found.
///   3. Validate run is in UNKNOWN state — 422 when not.
///   4. Parse resolution type — 400 when invalid.
///   5. Begin transaction.
///   6. Append ResolutionRecord.
///   7. For ApprovedNewRun: call CreateJobCommandHandler to create a new run;
///      link the new RunId to the ResolutionRecord. The original RunRecord is NEVER modified.
///   8. Append journal entry RUN_RESOLUTION_ADDED.
///   9. Commit atomically.
///
/// AUTO-REPLAY INVARIANT: the original RunRecord.State is NEVER changed.
/// UNKNOWN→QUEUED without a ResolutionRecord is forbidden in this codebase.
/// </summary>
public sealed class ResolveUnknownRunCommandHandler
{
    private readonly IRunRepository             _runRepo;
    private readonly IResolutionRepository      _resolutionRepo;
    private readonly IJournalRepository         _journalRepo;
    private readonly IUnitOfWork                _unitOfWork;
    private readonly IClock                     _clock;
    private readonly CreateJobCommandHandler    _createJobHandler;
    private readonly ILogger<ResolveUnknownRunCommandHandler> _logger;

    public ResolveUnknownRunCommandHandler(
        IRunRepository             runRepo,
        IResolutionRepository      resolutionRepo,
        IJournalRepository         journalRepo,
        IUnitOfWork                unitOfWork,
        IClock                     clock,
        CreateJobCommandHandler    createJobHandler,
        ILogger<ResolveUnknownRunCommandHandler> logger)
    {
        _runRepo          = runRepo;
        _resolutionRepo   = resolutionRepo;
        _journalRepo      = journalRepo;
        _unitOfWork       = unitOfWork;
        _clock            = clock;
        _createJobHandler = createJobHandler;
        _logger           = logger;
    }

    public async Task<Result<ResolveUnknownRunResult, Error>> HandleAsync(
        ResolveUnknownRunCommand cmd,
        CancellationToken cancellationToken = default)
    {
        // Guard: justification must not be empty (400).
        if (string.IsNullOrWhiteSpace(cmd.Justification))
            return Result<ResolveUnknownRunResult, Error>.Fail(
                Error.Validation("Justification must not be empty."));

        // Guard: operatorId must not be empty.
        if (string.IsNullOrWhiteSpace(cmd.OperatorId))
            return Result<ResolveUnknownRunResult, Error>.Fail(
                Error.Validation("OperatorId must not be empty."));

        // Parse resolution type (400 on invalid value).
        if (!TryParseResolutionType(cmd.ResolutionType, out ResolutionType resolutionType))
            return Result<ResolveUnknownRunResult, Error>.Fail(
                Error.Validation(
                    $"ResolutionType '{cmd.ResolutionType}' is not valid. " +
                    "Expected 'INVESTIGATED_NO_RERUN' or 'APPROVED_NEW_RUN'."));

        // Load run (404 when not found).
        var run = await _runRepo.GetByIdAsync(cmd.RunId, cancellationToken);
        if (run is null)
            return Result<ResolveUnknownRunResult, Error>.Fail(
                Error.NotFound($"Run '{cmd.RunId}' was not found."));

        // Guard: run must be in UNKNOWN state (422 when not).
        if (run.State != RunState.Unknown)
            return Result<ResolveUnknownRunResult, Error>.Fail(
                Error.Create("NOT_UNKNOWN_STATE",
                    $"Run '{cmd.RunId}' is in state {run.State.ToString().ToUpperInvariant()}, not UNKNOWN. " +
                    "Only UNKNOWN runs can be resolved."));

        var now = _clock.UtcNow;

        await _unitOfWork.BeginAsync(cancellationToken);

        Guid? newRunId = null;

        if (resolutionType == ResolutionType.ApprovedNewRun)
        {
            // Create a new run for the same script BEFORE building the ResolutionRecord
            // so we have the new RunId ready for the ResolutionRecord constructor,
            // which validates that ApprovedNewRun records must carry a non-null NewRunId.
            // The original RunRecord is NEVER modified — auto-replay invariant.
            var createCmd = new CreateJobCommand(
                ScriptArtifactId:    run.ScriptArtifactId,
                ScriptVersion:       run.ScriptVersion,
                SchemaVersion:       run.SchemaVersion,
                InputRefs:           run.InputRefs,
                SecretRefs:          run.SecretRefs,
                Deadline:            null,     // fresh run — no inherited deadline
                EventId:             null,     // no EventId dedup for operator-approved reruns
                TriggerType:         TriggerType.Manual,
                ContinuationScriptRef: null,
                BusinessJobId:       run.BusinessJobId);

            var createResult = await _createJobHandler.HandleAsync(createCmd, cancellationToken);
            if (createResult.IsFailure)
            {
                await _unitOfWork.RollbackAsync();
                return Result<ResolveUnknownRunResult, Error>.Fail(
                    Error.Create("NEW_RUN_CREATION_FAILED",
                        $"Could not create new run: {createResult.Error.Message}"));
            }

            newRunId = createResult.Value.RunId;

            _logger.LogInformation(
                "Resolution APPROVED_NEW_RUN: original run {OriginalRunId} stays UNKNOWN; new run {NewRunId} created.",
                cmd.RunId, newRunId);
        }
        else
        {
            _logger.LogInformation(
                "Resolution INVESTIGATED_NO_RERUN: run {RunId} stays UNKNOWN; no new run created.",
                cmd.RunId);
        }

        // Build and append the resolution record.
        // For ApprovedNewRun, newRunId is already set; the domain ctor validates it.
        var resolution = new ResolutionRecord(
            runId:         cmd.RunId,
            type:          resolutionType,
            justification: cmd.Justification,
            operatorId:    cmd.OperatorId,
            resolvedAt:    now,
            newRunId:      newRunId);

        await _resolutionRepo.AddAsync(resolution, cancellationToken);

        // Append audit journal entry.
        // NOTE: the original RunRecord is NOT saved/updated here — state stays UNKNOWN.
        // Use JsonSerializer to avoid JSON injection from operator-supplied operatorId.
        var journalPayload = newRunId.HasValue
            ? System.Text.Json.JsonSerializer.Serialize(new
                {
                    resolutionType = "APPROVED_NEW_RUN",
                    newRunId       = newRunId.Value.ToString(),
                    operatorId     = cmd.OperatorId,
                })
            : System.Text.Json.JsonSerializer.Serialize(new
                {
                    resolutionType = "INVESTIGATED_NO_RERUN",
                    operatorId     = cmd.OperatorId,
                });

        await _journalRepo.AppendAsync(
            new JournalEntry(
                runId:      cmd.RunId,
                attemptId:  run.AttemptId,
                eventType:  "RUN_RESOLUTION_ADDED",
                occurredAt: now,
                payload:    journalPayload),
            cancellationToken);

        await _unitOfWork.CommitAsync(cancellationToken);

        return Result<ResolveUnknownRunResult, Error>.Ok(
            new ResolveUnknownRunResult(resolution.Id, cmd.RunId, newRunId));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static bool TryParseResolutionType(string raw, out ResolutionType result)
    {
        result = default;
        switch (raw?.Trim().ToUpperInvariant())
        {
            case "INVESTIGATED_NO_RERUN":
                result = ResolutionType.InvestigatedNoRerun;
                return true;
            case "APPROVED_NEW_RUN":
                result = ResolutionType.ApprovedNewRun;
                return true;
            default:
                return false;
        }
    }
}
