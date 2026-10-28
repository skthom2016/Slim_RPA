#nullable enable
using Microsoft.EntityFrameworkCore;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Security;
using Tracto.Orchestrator.Infrastructure.Persistence;

namespace Tracto.Orchestrator.Infrastructure.Security;

/// <summary>
/// EF Core implementation of <see cref="IAdminSecurityEventRepository"/> (OC-13).
///
/// Finds security events associated with a run by scanning the <c>Details</c> column
/// for the run's GUID string. Security events are intentionally decoupled from run records
/// (no FK), so a text-search approach is used rather than a join.
///
/// Also implements <see cref="ISecurityEventReader"/> using the same strategy, so a single
/// class satisfies both read contracts.
/// </summary>
public sealed class EfAdminSecurityEventRepository : IAdminSecurityEventRepository, ISecurityEventReader
{
    private readonly OrchestratorDbContext _db;

    public EfAdminSecurityEventRepository(OrchestratorDbContext db)
    {
        _db = db;
    }

    // ── IAdminSecurityEventRepository + ISecurityEventReader ──────────────────
    // Both interfaces require the same method signature; a single implementation satisfies both.

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SecurityEvent>> FindByRunIdAsync(
        Guid              runId,
        CancellationToken cancellationToken = default)
        => await FindByRunIdCoreAsync(runId, cancellationToken);

    // ── Shared implementation ──────────────────────────────────────────────────

    private async Task<IReadOnlyList<SecurityEvent>> FindByRunIdCoreAsync(
        Guid              runId,
        CancellationToken cancellationToken)
    {
        var runIdString = runId.ToString();

        return await _db.SecurityEvents
            .AsNoTracking()
            .Where(e => e.Details.Contains(runIdString))
            .OrderBy(e => e.OccurredAt)
            .ToListAsync(cancellationToken);
    }
}
