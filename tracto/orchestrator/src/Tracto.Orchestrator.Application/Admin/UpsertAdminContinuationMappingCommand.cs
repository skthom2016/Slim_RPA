#nullable enable
using Microsoft.Extensions.Logging;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Journal;
using Tracto.Orchestrator.Domain.Runs;

namespace Tracto.Orchestrator.Application.Admin;

// ── Command ────────────────────────────────────────────────────────────────────

/// <summary>
/// Admin upsert for a continuation mapping (OC-13).
/// POST /admin/continuation-mappings
///
/// Behaviour:
///   • When a mapping for (TriggerRunId, TriggerState) already exists, its target fields
///     are updated and its Version is incremented.
///   • When no matching mapping exists, a new one is created at Version=1.
///   • A JournalEntry audit record is always written in the same atomic transaction.
///
/// Domain invariant 5: all writes committed in one EF Core transaction.
/// </summary>
public sealed record UpsertAdminContinuationMappingCommand(
    Guid?    TriggerRunId,
    RunState TriggerState,
    string   TargetArtifactId,
    string   TargetArtifactVersion,
    string?  InputTemplate = null);

// ── Result DTO ────────────────────────────────────────────────────────────────

public sealed record UpsertAdminContinuationMappingResult(
    Guid           MappingId,
    Guid?          TriggerRunId,
    string         TriggerState,
    string         TargetArtifactId,
    string         TargetArtifactVersion,
    string?        InputTemplate,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    int            Version,
    bool           WasCreated);

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// Handles <see cref="UpsertAdminContinuationMappingCommand"/> for OC-13.
///
/// Uses FindBestMatchAsync to locate an existing mapping for (TriggerRunId, TriggerState).
/// Updates the mapping if found; creates a new one otherwise.
/// Writes a JournalEntry audit record (using TriggerRunId when available, or a freshly
/// generated sentinel GUID for global mappings where TriggerRunId is null).
/// </summary>
public sealed class UpsertAdminContinuationMappingCommandHandler
{
    private static readonly RunState[] TerminalStates =
    [
        RunState.Succeeded, RunState.Failed, RunState.HandedOff,
        RunState.Cancelled, RunState.TimedOut, RunState.Unknown,
    ];

    private readonly IContinuationMappingRepository _mappingRepo;
    private readonly IJournalRepository             _journalRepo;
    private readonly IUnitOfWork                    _unitOfWork;
    private readonly IClock                         _clock;
    private readonly ILogger<UpsertAdminContinuationMappingCommandHandler> _logger;

    public UpsertAdminContinuationMappingCommandHandler(
        IContinuationMappingRepository mappingRepo,
        IJournalRepository             journalRepo,
        IUnitOfWork                    unitOfWork,
        IClock                         clock,
        ILogger<UpsertAdminContinuationMappingCommandHandler> logger)
    {
        _mappingRepo = mappingRepo;
        _journalRepo = journalRepo;
        _unitOfWork  = unitOfWork;
        _clock       = clock;
        _logger      = logger;
    }

    public async Task<Result<UpsertAdminContinuationMappingResult, Error>> HandleAsync(
        UpsertAdminContinuationMappingCommand command,
        CancellationToken cancellationToken = default)
    {
        // ── Validation ─────────────────────────────────────────────────────────

        if (!Array.Exists(TerminalStates, s => s == command.TriggerState))
            return Result<UpsertAdminContinuationMappingResult, Error>.Fail(
                Error.Validation($"TriggerState '{command.TriggerState}' is not a terminal state."));

        if (string.IsNullOrWhiteSpace(command.TargetArtifactId))
            return Result<UpsertAdminContinuationMappingResult, Error>.Fail(
                Error.Validation("TargetArtifactId must not be empty."));

        if (string.IsNullOrWhiteSpace(command.TargetArtifactVersion))
            return Result<UpsertAdminContinuationMappingResult, Error>.Fail(
                Error.Validation("TargetArtifactVersion must not be empty."));

        // ── Upsert logic ───────────────────────────────────────────────────────

        var now     = _clock.UtcNow;
        bool wasCreated;

        // FindBestMatchAsync returns the most-specific mapping for the given key.
        // For an admin upsert we want an exact match (same TriggerRunId and TriggerState),
        // which FindBestMatchAsync provides as its first priority.
        var existingMapping = await _mappingRepo.FindBestMatchAsync(
            sourceRunId:       command.TriggerRunId ?? Guid.Empty,
            triggerState:      command.TriggerState,
            cancellationToken: cancellationToken);

        // Discard the result if it is a global fallback when the command specifies a run-scoped mapping,
        // or vice versa — we only want an exact key match.
        if (existingMapping is not null &&
            existingMapping.TriggerRunId != command.TriggerRunId)
        {
            existingMapping = null;
        }

        ContinuationMapping mapping;

        if (existingMapping is not null)
        {
            // Update the existing mapping and increment its Version.
            existingMapping.Update(
                targetArtifactId:      command.TargetArtifactId,
                targetArtifactVersion: command.TargetArtifactVersion,
                inputTemplate:         command.InputTemplate);

            _mappingRepo.Update(existingMapping);
            mapping    = existingMapping;
            wasCreated = false;
        }
        else
        {
            // Create a brand-new mapping at Version=1.
            mapping = new ContinuationMapping(
                mappingId:             Guid.NewGuid(),
                triggerRunId:          command.TriggerRunId,
                triggerState:          command.TriggerState,
                targetArtifactId:      command.TargetArtifactId,
                targetArtifactVersion: command.TargetArtifactVersion,
                inputTemplate:         command.InputTemplate,
                createdAt:             now);

            await _mappingRepo.AddAsync(mapping, cancellationToken);
            wasCreated = true;
        }

        // ── Journal audit entry ────────────────────────────────────────────────
        // Use TriggerRunId when the mapping is run-scoped; otherwise mint a new GUID
        // so that the admin audit record has a stable, non-Guid.Empty primary key.
        var auditRunId = command.TriggerRunId ?? Guid.NewGuid();
        var eventType  = wasCreated
            ? "ADMIN_CONTINUATION_MAPPING_CREATED"
            : "ADMIN_CONTINUATION_MAPPING_UPDATED";

        var journalPayload = System.Text.Json.JsonSerializer.Serialize(new
        {
            mappingId             = mapping.MappingId.ToString(),
            triggerRunId          = command.TriggerRunId?.ToString(),
            triggerState          = command.TriggerState.ToString().ToUpperInvariant(),
            targetArtifactId      = command.TargetArtifactId,
            targetArtifactVersion = command.TargetArtifactVersion,
            version               = mapping.Version,
        });

        var journalEntry = new JournalEntry(
            runId:      auditRunId,
            attemptId:  null,
            eventType:  eventType,
            occurredAt: now,
            payload:    journalPayload);

        // ── Atomic commit ──────────────────────────────────────────────────────

        await _unitOfWork.BeginAsync(cancellationToken);
        var committed = false;
        try
        {
            await _journalRepo.AppendAsync(journalEntry, cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);
            committed = true;
        }
        finally
        {
            if (!committed)
                await _unitOfWork.RollbackAsync();
        }

        _logger.LogInformation(
            "Admin continuation mapping {MappingId} {Action} for TriggerState={TriggerState} " +
            "TriggerRunId={TriggerRunId} Artifact={ArtifactId}@{Version} Version={MappingVersion}.",
            mapping.MappingId, wasCreated ? "created" : "updated",
            mapping.TriggerState, mapping.TriggerRunId,
            mapping.TargetArtifactId, mapping.TargetArtifactVersion, mapping.Version);

        return Result<UpsertAdminContinuationMappingResult, Error>.Ok(
            new UpsertAdminContinuationMappingResult(
                MappingId:             mapping.MappingId,
                TriggerRunId:          mapping.TriggerRunId,
                TriggerState:          mapping.TriggerState.ToString().ToUpperInvariant(),
                TargetArtifactId:      mapping.TargetArtifactId,
                TargetArtifactVersion: mapping.TargetArtifactVersion,
                InputTemplate:         mapping.InputTemplate,
                CreatedAt:             mapping.CreatedAt,
                UpdatedAt:             wasCreated ? null : now,
                Version:               mapping.Version,
                WasCreated:            wasCreated));
    }
}
