#nullable enable
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Runs;

namespace Tracto.Orchestrator.Application.Jobs;

// ── Query ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Retrieves a continuation mapping by its primary key (OC-08).
/// GET /continuation-mappings/{id}
/// </summary>
public sealed record GetContinuationMappingQuery(Guid MappingId);

// ── Result DTO ────────────────────────────────────────────────────────────────

/// <summary>Read-model returned for a single continuation mapping.</summary>
public sealed record GetContinuationMappingResult(
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
/// Handles <see cref="GetContinuationMappingQuery"/> — story OC-08.
/// Read-only; no transaction required.
/// </summary>
public sealed class GetContinuationMappingQueryHandler
{
    private readonly IContinuationMappingRepository _mappingRepo;

    public GetContinuationMappingQueryHandler(IContinuationMappingRepository mappingRepo)
        => _mappingRepo = mappingRepo;

    public async Task<Result<GetContinuationMappingResult, Error>> HandleAsync(
        GetContinuationMappingQuery query,
        CancellationToken           cancellationToken = default)
    {
        var mapping = await _mappingRepo.GetByIdAsync(query.MappingId, cancellationToken);
        if (mapping is null)
            return Result<GetContinuationMappingResult, Error>.Fail(
                Error.NotFound($"ContinuationMapping '{query.MappingId}' not found."));

        return Result<GetContinuationMappingResult, Error>.Ok(
            new GetContinuationMappingResult(
                MappingId:             mapping.MappingId,
                TriggerRunId:          mapping.TriggerRunId,
                TriggerState:          mapping.TriggerState.ToString().ToUpperInvariant(),
                TargetArtifactId:      mapping.TargetArtifactId,
                TargetArtifactVersion: mapping.TargetArtifactVersion,
                InputTemplate:         mapping.InputTemplate,
                CreatedAt:             mapping.CreatedAt,
                Version:               mapping.Version));
    }
}
