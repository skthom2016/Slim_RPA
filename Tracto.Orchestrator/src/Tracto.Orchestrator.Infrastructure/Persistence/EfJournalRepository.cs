using Microsoft.EntityFrameworkCore;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Journal;

namespace Tracto.Orchestrator.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IJournalRepository"/>.
/// Append-only; never updates or deletes entries.
/// </summary>
public sealed class EfJournalRepository : IJournalRepository
{
    private readonly OrchestratorDbContext _db;

    public EfJournalRepository(OrchestratorDbContext db) => _db = db;

    public async Task AppendAsync(JournalEntry entry, CancellationToken cancellationToken = default)
        => await _db.JournalEntries.AddAsync(entry, cancellationToken);

    public async Task<IReadOnlyList<JournalEntry>> GetByRunIdAsync(Guid runId, CancellationToken cancellationToken = default)
        => await _db.JournalEntries
                    .Where(j => j.RunId == runId)
                    .OrderBy(j => j.OccurredAt)
                    .ToListAsync(cancellationToken);

    public async Task<bool> HasEntriesOfTypeAsync(
        Guid runId,
        string eventType,
        CancellationToken cancellationToken = default)
        => await _db.JournalEntries
                    .AnyAsync(j => j.RunId == runId && j.EventType == eventType,
                              cancellationToken);
}
