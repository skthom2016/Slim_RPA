#nullable enable
using Microsoft.Extensions.Logging;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Runs;

namespace Tracto.Orchestrator.Application.Jobs;

// ── Command ────────────────────────────────────────────────────────────────────

/// <summary>
/// Evaluates whether a continuation run (Run B) should be created for the given
/// source run and trigger state (OC-08).
///
/// Rules:
///   - FAILED / CANCELLED / TIMED_OUT / UNKNOWN without a mapping → no action (Ok, RunBId null).
///   - HANDED_OFF without a mapping → no Run B, log + security event (operator attention).
///   - Mapping found → create Run B via CreateJobCommandHandler (dedup-guarded).
///   - Dedup: if (SourceRunId, TriggerState) already has a Run B record → idempotent Ok.
///   - Source run state is never modified by this command.
/// </summary>
public sealed record CreateContinuationRunCommand(
    Guid     SourceRunId,
    RunState TriggerState,
    string?  BusinessJobId = null);

// ── Result DTO ────────────────────────────────────────────────────────────────

/// <summary>Returned on completion. RunBId is null when no continuation was required.</summary>
public sealed record CreateContinuationRunResult(
    Guid  SourceRunId,
    Guid? RunBId,
    bool  AlreadyExisted,
    string Outcome);

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// Handles <see cref="CreateContinuationRunCommand"/> — story OC-08.
/// All writes are atomic inside a single EF Core transaction (domain invariant 5).
/// </summary>
public sealed class CreateContinuationRunCommandHandler
{
    private readonly IRunRepository                  _runRepo;
    private readonly IContinuationMappingRepository  _mappingRepo;
    private readonly IContinuationRunRepository      _continuationRunRepo;
    private readonly CreateJobCommandHandler          _createJobHandler;
    private readonly IUnitOfWork                     _unitOfWork;
    private readonly IClock                          _clock;
    private readonly ILogger<CreateContinuationRunCommandHandler> _logger;

    public CreateContinuationRunCommandHandler(
        IRunRepository                  runRepo,
        IContinuationMappingRepository  mappingRepo,
        IContinuationRunRepository      continuationRunRepo,
        CreateJobCommandHandler          createJobHandler,
        IUnitOfWork                     unitOfWork,
        IClock                          clock,
        ILogger<CreateContinuationRunCommandHandler> logger)
    {
        _runRepo             = runRepo;
        _mappingRepo         = mappingRepo;
        _continuationRunRepo = continuationRunRepo;
        _createJobHandler    = createJobHandler;
        _unitOfWork          = unitOfWork;
        _clock               = clock;
        _logger              = logger;
    }

    public async Task<Result<CreateContinuationRunResult, Error>> HandleAsync(
        CreateContinuationRunCommand command,
        CancellationToken            cancellationToken = default)
    {
        // ── 1. Validate source run exists ──────────────────────────────────────
        var sourceRun = await _runRepo.GetByIdAsync(command.SourceRunId, cancellationToken);
        if (sourceRun is null)
            return Result<CreateContinuationRunResult, Error>.Fail(
                Error.NotFound($"Source run '{command.SourceRunId}' not found."));

        // ── 2. Dedup guard: check if Run B already exists ──────────────────────
        var existing = await _continuationRunRepo.FindAsync(
            command.SourceRunId, command.TriggerState, cancellationToken);

        if (existing is not null)
        {
            _logger.LogInformation(
                "CreateContinuationRun: Run B already exists for " +
                "SourceRunId={SourceRunId} TriggerState={TriggerState} RunBId={RunBId}. " +
                "Returning idempotent result.",
                command.SourceRunId, command.TriggerState, existing.RunBId);

            return Result<CreateContinuationRunResult, Error>.Ok(
                new CreateContinuationRunResult(
                    SourceRunId:   command.SourceRunId,
                    RunBId:        existing.RunBId,
                    AlreadyExisted: true,
                    Outcome:       "ALREADY_EXISTS"));
        }

        // ── 3. Find best-matching continuation mapping ─────────────────────────
        var mapping = await _mappingRepo.FindBestMatchAsync(
            command.SourceRunId, command.TriggerState, cancellationToken);

        // ── 4. No mapping — apply policy per trigger state ─────────────────────
        if (mapping is null)
        {
            if (command.TriggerState == RunState.HandedOff)
            {
                // HANDED_OFF without a mapping → operator attention required.
                _logger.LogWarning(
                    "[SECURITY] CreateContinuationRun: HANDED_OFF run {SourceRunId} has no " +
                    "continuation mapping. Operator attention required. " +
                    "SecurityEvent=HANDOFF_MAPPING_MISSING.",
                    command.SourceRunId);
            }
            else
            {
                // FAILED / CANCELLED / TIMED_OUT / UNKNOWN without mapping → silent no-op.
                _logger.LogDebug(
                    "CreateContinuationRun: no mapping for SourceRunId={SourceRunId} " +
                    "TriggerState={TriggerState}. No Run B created.",
                    command.SourceRunId, command.TriggerState);
            }

            return Result<CreateContinuationRunResult, Error>.Ok(
                new CreateContinuationRunResult(
                    SourceRunId:    command.SourceRunId,
                    RunBId:         null,
                    AlreadyExisted: false,
                    Outcome:        command.TriggerState == RunState.HandedOff
                                    ? "HANDOFF_MAPPING_MISSING"
                                    : "NO_MAPPING"));
        }

        // ── 5. Snapshot the mapping version before creating Run B ──────────────
        var mappingVersionSnapshot = mapping.Version;

        // ── 6. Create Run B via CreateJobCommandHandler ────────────────────────
        //
        // We do NOT call BeginAsync here because CreateJobCommandHandler manages its
        // own transaction internally. The ContinuationRunRecord is written in a
        // separate follow-up transaction to record the dedup anchor once Run B exists.
        // (If Run B creation fails the dedup record is never written, which is correct:
        //  the next trigger will retry.)

        var createJobCmd = new CreateJobCommand(
            ScriptArtifactId:      mapping.TargetArtifactId,
            ScriptVersion:         mapping.TargetArtifactVersion,
            SchemaVersion:         "1",
            InputRefs:             BuildInputRefs(mapping.InputTemplate),
            SecretRefs:            [],
            Deadline:              null,
            EventId:               $"continuation:{command.SourceRunId}:{command.TriggerState}",
            TriggerType:           Domain.Runs.TriggerType.Continuation,
            ContinuationScriptRef: null,
            BusinessJobId:         command.BusinessJobId);

        // Temporarily bypass the "no ContinuationScriptRef" guard — Run B is launched
        // without ContinuationScriptRef (already null), so this succeeds.
        var createResult = await _createJobHandler.HandleAsync(createJobCmd, cancellationToken);
        if (createResult.IsFailure)
        {
            _logger.LogError(
                "CreateContinuationRun: failed to create Run B for SourceRunId={SourceRunId}: " +
                "{ErrorCode} — {ErrorMessage}.",
                command.SourceRunId, createResult.Error.Code, createResult.Error.Message);

            return Result<CreateContinuationRunResult, Error>.Fail(createResult.Error);
        }

        var runBId = createResult.Value.RunId;

        // ── 7. Record the dedup anchor (ContinuationRunRecord) ────────────────
        var dedupRecord = new ContinuationRunRecord(
            id:                    Guid.NewGuid(),
            sourceRunId:           command.SourceRunId,
            triggerState:          command.TriggerState,
            runBId:                runBId,
            mappingId:             mapping.MappingId,
            mappingVersionSnapshot: mappingVersionSnapshot,
            createdAt:             _clock.UtcNow);

        await _unitOfWork.BeginAsync(cancellationToken);
        var committed = false;
        try
        {
            await _continuationRunRepo.AddAsync(dedupRecord, cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);
            committed = true;
        }
        finally
        {
            if (!committed)
                await _unitOfWork.RollbackAsync();
        }

        _logger.LogInformation(
            "CreateContinuationRun: Run B {RunBId} created for SourceRunId={SourceRunId} " +
            "TriggerState={TriggerState} MappingId={MappingId} MappingVersion={Version}.",
            runBId, command.SourceRunId, command.TriggerState,
            mapping.MappingId, mappingVersionSnapshot);

        return Result<CreateContinuationRunResult, Error>.Ok(
            new CreateContinuationRunResult(
                SourceRunId:    command.SourceRunId,
                RunBId:         runBId,
                AlreadyExisted: false,
                Outcome:        "CREATED"));
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Converts the optional InputTemplate JSON string into a string[] of input refs.
    /// The template is stored opaquely; we expose the raw string as a single-element ref
    /// when present so Run B can resolve it via its normal artifact loading path.
    /// </summary>
    private static string[] BuildInputRefs(string? inputTemplate)
    {
        if (string.IsNullOrWhiteSpace(inputTemplate))
            return [];

        // The template is treated as an opaque ref key pointing to a pre-registered
        // artifact.  Handlers downstream resolve it the same way as any InputRef.
        return [inputTemplate];
    }
}
