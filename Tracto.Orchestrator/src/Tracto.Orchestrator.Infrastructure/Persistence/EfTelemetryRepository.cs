#nullable enable
using Microsoft.EntityFrameworkCore;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Telemetry;

namespace Tracto.Orchestrator.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="ITelemetryRepository"/> (OC-14).
///
/// Implements both Domain.Telemetry.ITelemetryRepository and
/// Application.Abstractions.ITelemetryRepository so both can be resolved from DI.
///
/// Writes are tracked against the ambient OrchestratorDbContext shared with UnitOfWork,
/// so AddAsync is committed atomically with the triggering state change when
/// IUnitOfWork.CommitAsync flushes (domain invariant 5).
///
/// Reads are append-only projections — the telemetry table is never updated or deleted.
/// </summary>
public sealed class EfTelemetryRepository :
    Domain.Telemetry.ITelemetryRepository,
    Application.Abstractions.ITelemetryRepository
{
    private readonly OrchestratorDbContext _db;

    public EfTelemetryRepository(OrchestratorDbContext db) => _db = db;

    /// <inheritdoc/>
    public Task AddAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
    {
        // Use the synchronous Add() — not AddAsync() — because TelemetryEvent uses a
        // client-generated GUID key (ValueGeneratedNever). AddAsync is only needed when
        // the database generates the key value asynchronously (e.g. HiLo sequences).
        _db.TelemetryEvents.Add(telemetryEvent);
        // SaveChanges is NOT called here; the UnitOfWork.CommitAsync flushes the
        // row together with all other tracked changes in the same transaction.
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TelemetryEvent>> GetByRunIdAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
        => await _db.TelemetryEvents
                    .AsNoTracking()          // read-only; telemetry rows are immutable — no tracking needed
                    .Where(e => e.RunId == runId)
                    .OrderBy(e => e.OccurredAt)
                    .ToListAsync(cancellationToken);
}