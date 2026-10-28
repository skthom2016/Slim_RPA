#nullable enable
using Microsoft.EntityFrameworkCore;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Security;
using Tracto.Orchestrator.Infrastructure.Persistence;

namespace Tracto.Orchestrator.Infrastructure.Security;

/// <summary>
/// EF Core implementation of <see cref="ISecurityEventReader"/> (OC-13).
///
/// Locates security events related to a run by searching for the run's GUID string inside
/// the <c>Details</c> column. This avoids adding a RunId FK to the SecurityEvent table
/// (security events are intentionally decoupled from run records).
/// </summary>
public sealed class EfSecurityEventReader : ISecurityEventReader
{
    private readonly OrchestratorDbContext _db;

    public EfSecurityEventReader(OrchestratorDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SecurityEvent>> FindByRunIdAsync(
        Guid              runId,
        CancellationToken cancellationToken = default)
    {
        var runIdString = runId.ToString();

        return await _db.SecurityEvents
            .AsNoTracking()
            .Where(e => e.Details.Contains(runIdString))
            .OrderBy(e => e.OccurredAt)
            .ToListAsync(cancellationToken);
    }
}
