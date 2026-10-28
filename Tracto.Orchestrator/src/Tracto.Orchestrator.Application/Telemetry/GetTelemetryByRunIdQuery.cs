#nullable enable
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Common;
using AppITelemetryRepository = Tracto.Orchestrator.Application.Abstractions.ITelemetryRepository;

namespace Tracto.Orchestrator.Application.Telemetry;

// ── Query ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Returns all telemetry events for a run, ordered by OccurredAt ascending.
/// Used by the telemetry export endpoint (OC-14) and by admin dashboards.
/// </summary>
/// <param name="RunId">Primary key of the run whose events are requested.</param>
public sealed record GetTelemetryByRunIdQuery(Guid RunId);

// ── Result DTO ────────────────────────────────────────────────────────────────

/// <summary>Single telemetry event row returned by <see cref="GetTelemetryByRunIdQueryHandler"/>.</summary>
public sealed record TelemetryEventDto(
    Guid           Id,
    Guid           RunId,
    string?        WorkerId,
    Guid?          LeaseId,
    Guid?          AttemptId,
    string         EventType,
    DateTimeOffset OccurredAt,
    string?        Metadata);

/// <summary>Paged result returned to the caller.</summary>
public sealed record GetTelemetryByRunIdResponse(
    Guid                         RunId,
    IReadOnlyList<TelemetryEventDto> Events);

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// Handles <see cref="GetTelemetryByRunIdQuery"/> — story OC-14.
///
/// This is a read-only query: no UnitOfWork, no state mutations.
/// The handler projects <see cref="TelemetryEvent"/> domain objects to
/// <see cref="TelemetryEventDto"/> DTOs so the API layer never exposes
/// internal domain types directly.
///
/// The query succeeds even when no events exist for the run (returns empty list),
/// because the absence of telemetry is itself valid observable state.
/// </summary>
public sealed class GetTelemetryByRunIdQueryHandler
{
    private readonly AppITelemetryRepository _telemetryRepo;

    public GetTelemetryByRunIdQueryHandler(AppITelemetryRepository telemetryRepo)
    {
        _telemetryRepo = telemetryRepo;
    }

    public async Task<Result<GetTelemetryByRunIdResponse, Error>> HandleAsync(
        GetTelemetryByRunIdQuery query,
        CancellationToken        cancellationToken = default)
    {
        if (query.RunId == Guid.Empty)
            return Result<GetTelemetryByRunIdResponse, Error>.Fail(
                Error.Validation("RunId must not be empty."));

        var events = await _telemetryRepo.GetByRunIdAsync(query.RunId, cancellationToken);

        var dtos = events
            .Select(e => new TelemetryEventDto(
                Id:         e.Id,
                RunId:      e.RunId,
                WorkerId:   e.WorkerId,
                LeaseId:    e.LeaseId,
                AttemptId:  e.AttemptId,
                EventType:  e.EventType,
                OccurredAt: e.OccurredAt,
                Metadata:   e.Metadata))
            .ToList()
            .AsReadOnly();

        return Result<GetTelemetryByRunIdResponse, Error>.Ok(
            new GetTelemetryByRunIdResponse(query.RunId, dtos));
    }
}

