#nullable enable
using Microsoft.Extensions.Logging;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Runs;

namespace Tracto.Orchestrator.Application.Jobs;

// ── Command ────────────────────────────────────────────────────────────────────

/// <summary>
/// Creates a new continuation mapping (OC-08).
/// POST /continuation-mappings
/// </summary>
/// <param name="TriggerRunId">
/// Optional source run. When null this is a global (state-level) mapping.
/// </param>
/// <param name="TriggerState">
/// The terminal state that triggers Run B. Must be one of the terminal RunState values.
/// </param>
/// <param name="TargetArtifactId">ArtifactId of the script to run as Run B.</param>
/// <param name="TargetArtifactVersion">Version of the script artifact.</param>
/// <param name="InputTemplate">Optional JSON template for Run B's input refs.</param>
public sealed record CreateContinuationMappingCommand(
    Guid?    TriggerRunId,
    RunState TriggerState,
    string   TargetArtifactId,
    string   TargetArtifactVersion,
    string?  InputTemplate = null);

// ── Result DTO ────────────────────────────────────────────────────────────────

/// <summary>Returned on a successful mapping creation.</summary>
public sealed record CreateContinuationMappingResult(
    Guid     MappingId,
    Guid?    TriggerRunId,
    string   TriggerState,
    string   TargetArtifactId,
    string   TargetArtifactVersion,
    string?  InputTemplate,
    DateTimeOffset CreatedAt,
    int      Version);

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// Handles <see cref="CreateContinuationMappingCommand"/> — story OC-08.
/// Validates the trigger state is terminal and the artifact fields are non-empty,
/// then persists the new mapping atomically.
/// </summary>
public sealed class CreateContinuationMappingCommandHandler
{
    private static readonly RunState[] TerminalStates =
    [
        RunState.Succeeded, RunState.Failed, RunState.HandedOff,
        RunState.Cancelled, RunState.TimedOut, RunState.Unknown
    ];

    private readonly IContinuationMappingRepository _mappingRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock      _clock;
    private readonly ILogger<CreateContinuationMappingCommandHandler> _logger;

    public CreateContinuationMappingCommandHandler(
        IContinuationMappingRepository mappingRepo,
        IUnitOfWork unitOfWork,
        IClock      clock,
        ILogger<CreateContinuationMappingCommandHandler> logger)
    {
        _mappingRepo = mappingRepo;
        _unitOfWork  = unitOfWork;
        _clock       = clock;
        _logger      = logger;
    }

    public async Task<Result<CreateContinuationMappingResult, Error>> HandleAsync(
        CreateContinuationMappingCommand command,
        CancellationToken cancellationToken = default)
    {
        // ── Validation ─────────────────────────────────────────────────────────

        if (!Array.Exists(TerminalStates, s => s == command.TriggerState))
            return Result<CreateContinuationMappingResult, Error>.Fail(
                Error.Validation($"TriggerState '{command.TriggerState}' is not a terminal state."));

        if (string.IsNullOrWhiteSpace(command.TargetArtifactId))
            return Result<CreateContinuationMappingResult, Error>.Fail(
                Error.Validation("TargetArtifactId must not be empty."));

        if (string.IsNullOrWhiteSpace(command.TargetArtifactVersion))
            return Result<CreateContinuationMappingResult, Error>.Fail(
                Error.Validation("TargetArtifactVersion must not be empty."));

        // ── Persist ────────────────────────────────────────────────────────────

        var now     = _clock.UtcNow;
        var mapping = new ContinuationMapping(
            mappingId:            Guid.NewGuid(),
            triggerRunId:         command.TriggerRunId,
            triggerState:         command.TriggerState,
            targetArtifactId:     command.TargetArtifactId,
            targetArtifactVersion: command.TargetArtifactVersion,
            inputTemplate:        command.InputTemplate,
            createdAt:            now);

        await _unitOfWork.BeginAsync(cancellationToken);
        var committed = false;
        try
        {
            await _mappingRepo.AddAsync(mapping, cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);
            committed = true;
        }
        finally
        {
            if (!committed)
                await _unitOfWork.RollbackAsync();
        }

        _logger.LogInformation(
            "ContinuationMapping {MappingId} created for TriggerState={TriggerState} " +
            "TriggerRunId={TriggerRunId} Artifact={ArtifactId}@{Version}.",
            mapping.MappingId, mapping.TriggerState,
            mapping.TriggerRunId, mapping.TargetArtifactId, mapping.TargetArtifactVersion);

        return Result<CreateContinuationMappingResult, Error>.Ok(
            new CreateContinuationMappingResult(
                MappingId:            mapping.MappingId,
                TriggerRunId:         mapping.TriggerRunId,
                TriggerState:         mapping.TriggerState.ToString().ToUpperInvariant(),
                TargetArtifactId:     mapping.TargetArtifactId,
                TargetArtifactVersion: mapping.TargetArtifactVersion,
                InputTemplate:        mapping.InputTemplate,
                CreatedAt:            mapping.CreatedAt,
                Version:              mapping.Version));
    }
}
