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

// ── Command ───────────────────────────────────────────────────────────────────

/// <summary>
/// Represents a request to create and immediately queue a new automation job.
/// ContinuationScriptRef must be absent — reserved for future handoff continuation.
/// </summary>
public sealed record CreateJobCommand(
    string       ScriptArtifactId,
    string       ScriptVersion,
    string       SchemaVersion,
    string[]     InputRefs,
    string[]     SecretRefs,
    DateTime?    Deadline,
    string?      EventId,
    TriggerType  TriggerType,
    string?      ContinuationScriptRef,
    string?      BusinessJobId = null);

// ── Result DTO ────────────────────────────────────────────────────────────────

/// <summary>Returned on success: the new (or existing, for idempotent EventId) run.</summary>
public sealed record CreateJobResult(Guid RunId, string State);

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// Handles <see cref="CreateJobCommand"/>:
///   1. Guards: ContinuationScriptRef forbidden, SchemaVersion must be "1",
///              artifact must exist, deadline must not be in the past or exactly now.
///   2. EventId dedup: returns existing run within 5-minute window (idempotent 200).
///   3. Creates RunRecord (CREATED), emits journal entry, transitions to QUEUED,
///      emits journal entry, commits atomically (invariant 5).
/// </summary>
public sealed class CreateJobCommandHandler
{
    /// <summary>Deduplication window for callback-triggered jobs.</summary>
    public static readonly TimeSpan EventIdDedupWindow = TimeSpan.FromMinutes(5);

    private readonly IRunRepository      _runRepo;
    private readonly IArtifactRepository _artifactRepo;
    private readonly IJournalRepository  _journalRepo;
    private readonly IUnitOfWork         _unitOfWork;
    private readonly IClock              _clock;
    private readonly ITelemetryService   _telemetry;
    private readonly ILogger<CreateJobCommandHandler> _logger;

    public CreateJobCommandHandler(
        IRunRepository             runRepo,
        IArtifactRepository        artifactRepo,
        IJournalRepository         journalRepo,
        IUnitOfWork                unitOfWork,
        IClock                     clock,
        ITelemetryService          telemetry,
        ILogger<CreateJobCommandHandler> logger)
    {
        _runRepo      = runRepo;
        _artifactRepo = artifactRepo;
        _journalRepo  = journalRepo;
        _unitOfWork   = unitOfWork;
        _clock        = clock;
        _telemetry    = telemetry;
        _logger       = logger;
    }

    public async Task<Result<CreateJobResult, Error>> HandleAsync(
        CreateJobCommand cmd,
        CancellationToken cancellationToken = default)
    {
        // Guard: ContinuationScriptRef is reserved for future handoff; always forbidden here.
        if (!string.IsNullOrWhiteSpace(cmd.ContinuationScriptRef))
            return Result<CreateJobResult, Error>.Fail(
                Error.Create("CONTINUATION_SCRIPT_REF_FORBIDDEN",
                             "CONTINUATION_SCRIPT_REF_FORBIDDEN"));

        // Guard: only schema version "1" is supported.
        if (cmd.SchemaVersion != "1")
            return Result<CreateJobResult, Error>.Fail(
                Error.Create("UNSUPPORTED_SCHEMA",
                             $"Schema version '{cmd.SchemaVersion}' is not supported. Expected '1'."));

        // Guard: artifact must exist and must not be soft-deleted.
        var artifact = await _artifactRepo.GetRecordAsync(
            cmd.ScriptArtifactId, cmd.ScriptVersion, cancellationToken);
        if (artifact is null || artifact.IsDeleted)
            return Result<CreateJobResult, Error>.Fail(
                Error.Create("ARTIFACT_NOT_FOUND",
                             $"Artifact '{cmd.ScriptArtifactId}' version '{cmd.ScriptVersion}' was not found."));

        var now = _clock.UtcNow;

        // Guard: deadline must be strictly in the future (deadline <= now is rejected).
        if (cmd.Deadline.HasValue && cmd.Deadline.Value <= now.UtcDateTime)
            return Result<CreateJobResult, Error>.Fail(
                Error.Create("DEADLINE_IN_PAST",
                             $"Deadline {cmd.Deadline.Value:O} is not in the future."));

        // EventId deduplication: return the existing run if found within the window.
        if (!string.IsNullOrWhiteSpace(cmd.EventId))
        {
            var notBefore = now - EventIdDedupWindow;
            var existing  = await _runRepo.FindByEventIdAsync(cmd.EventId, notBefore, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Duplicate EventId {EventId} within dedup window; returning existing run {RunId}.",
                    cmd.EventId, existing.Id);
                return Result<CreateJobResult, Error>.Ok(
                    new CreateJobResult(existing.Id, existing.State.ToString().ToUpperInvariant()));
            }
        }

        // Atomic write: RunRecord (CREATED -> QUEUED) + two journal entries.
        await _unitOfWork.BeginAsync(cancellationToken);

        var runId = Guid.NewGuid();
        var run   = new RunRecord(
            id:               runId,
            workflowId:       cmd.ScriptArtifactId,
            createdAt:        now,
            correlationId:    Guid.NewGuid(),
            scriptArtifactId: cmd.ScriptArtifactId,
            scriptVersion:    cmd.ScriptVersion,
            schemaVersion:    cmd.SchemaVersion,
            inputRefs:        cmd.InputRefs,
            secretRefs:       cmd.SecretRefs,
            deadline:         cmd.Deadline,
            eventId:          cmd.EventId,
            triggerType:      cmd.TriggerType,
            businessJobId:    cmd.BusinessJobId);

        await _runRepo.AddAsync(run, cancellationToken);

        await _journalRepo.AppendAsync(
            new JournalEntry(run.Id, run.AttemptId, "RUN_CREATED", now),
            cancellationToken);

        // Telemetry: RUN_CREATED
        Activity.Current?.SetTag("run.id", run.Id.ToString());
        Activity.Current?.SetTag("attempt.id", run.AttemptId.ToString());
        await _telemetry.EmitAsync(new TelemetryEvent(
            id:        Guid.NewGuid(),
            runId:     run.Id,
            eventType: TelemetryEventType.RunCreated,
            occurredAt: now,
            metadata:  JsonSerializer.Serialize(new
            {
                workflowId   = run.WorkflowId,
                triggerType  = cmd.TriggerType.ToString(),
                inputRefCount  = cmd.InputRefs.Length,
                secretRefCount = cmd.SecretRefs.Length,
                secretRefs     = "REDACTED",
            })),
            cancellationToken);

        // Transition CREATED -> QUEUED.
        run.Enqueue(now);
        _runRepo.Update(run);

        await _journalRepo.AppendAsync(
            new JournalEntry(run.Id, run.AttemptId, "RUN_QUEUED", now),
            cancellationToken);

        // Telemetry: RUN_QUEUED
        await _telemetry.EmitAsync(new TelemetryEvent(
            id:        Guid.NewGuid(),
            runId:     run.Id,
            eventType: TelemetryEventType.RunQueued,
            occurredAt: now),
            cancellationToken);

        await _unitOfWork.CommitAsync(cancellationToken);

        _logger.LogInformation("Job {RunId} created and queued (trigger={Trigger}).",
            run.Id, cmd.TriggerType);

        return Result<CreateJobResult, Error>.Ok(new CreateJobResult(run.Id, "QUEUED"));
    }
}
