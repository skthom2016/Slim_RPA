#nullable enable
using Microsoft.EntityFrameworkCore;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Runs;

namespace Tracto.Orchestrator.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of IAdminJobRepository and IAdminRunQueryRepository (OC-13).
/// All queries push filtering and paging to the database via LINQ-to-EF.
/// SecretRefs are never included in any projection — domain invariant 4.
/// </summary>
public sealed class EfAdminJobRepository : IAdminJobRepository, IAdminRunQueryRepository
{
    private readonly OrchestratorDbContext _db;

    public EfAdminJobRepository(OrchestratorDbContext db) => _db = db;

    public async Task<AdminJobPage> ListJobsAsync(
        RunState?       state,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int             page,
        int             pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _db.RunRecords.AsNoTracking();
        if (state.HasValue)  query = query.Where(r => r.State == state.Value);
        if (from.HasValue)   query = query.Where(r => r.CreatedAt >= from.Value);
        if (to.HasValue)     query = query.Where(r => r.CreatedAt <= to.Value);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new AdminJobSummary(r.Id, r.State.ToString().ToUpper(), r.CreatedAt, r.NeedsAttention))
            .ToListAsync(cancellationToken);

        return new AdminJobPage(total, items);
    }

    public async Task<AdminJobDetailView?> GetJobDetailAsync(
        Guid              runId,
        CancellationToken cancellationToken = default)
    {
        var run = await _db.RunRecords.AsNoTracking()
                           .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null) return null;

        var finalResult = await _db.FinalResultRecords.AsNoTracking()
                                   .FirstOrDefaultAsync(f => f.RunId == runId, cancellationToken);

        var journalEntries = await _db.JournalEntries.AsNoTracking()
            .Where(j => j.RunId == runId)
            .OrderBy(j => j.OccurredAt)
            .Select(j => new AdminJournalEntryView(j.Id, j.EventType, j.OccurredAt, j.Payload))
            .ToListAsync(cancellationToken);

        AdminFinalResultView? adminFinalResult = null;
        if (finalResult is not null)
        {
            adminFinalResult = new AdminFinalResultView(
                Id:              finalResult.Id,
                TerminalState:   finalResult.TerminalState.ToString().ToUpperInvariant(),
                ErrorCategory:   finalResult.ErrorCategory,
                ErrorMessage:    finalResult.ErrorMessage,
                BusinessOutcome: finalResult.BusinessOutcome,
                EvidenceRefs:    finalResult.EvidenceRefs,
                BusinessJobId:   finalResult.BusinessJobId,
                CompletedAt:     finalResult.CompletedAt,
                NeedsAttention:  finalResult.NeedsAttention,
                UnknownReason:   finalResult.UnknownReason);
        }

        return new AdminJobDetailView(
            RunId:             run.Id,
            WorkflowId:        run.WorkflowId,
            State:             run.State.ToString().ToUpperInvariant(),
            AttemptId:         run.AttemptId,
            CreatedAt:         run.CreatedAt,
            UpdatedAt:         run.UpdatedAt,
            NeedsAttention:    run.NeedsAttention,
            UnknownReason:     run.UnknownReason,
            WorkerId:          run.WorkerId,
            CorrelationId:     run.CorrelationId,
            ScriptArtifactId:  run.ScriptArtifactId,
            ScriptVersion:     run.ScriptVersion,
            SchemaVersion:     run.SchemaVersion,
            InputRefs:         run.InputRefs,
            Deadline:          run.Deadline,
            EventId:           run.EventId,
            TriggerType:       run.TriggerType.ToString().ToUpperInvariant(),
            BusinessJobId:     run.BusinessJobId,
            FinalResult:       adminFinalResult,
            ResolutionEntries: journalEntries);
    }

    public async Task<IReadOnlyList<RunRecord>> FindByStateAsync(RunState state, CancellationToken ct = default)
        => await _db.RunRecords.AsNoTracking().Where(r => r.State == state).ToListAsync(ct);

    public async Task<RunRecord?> GetByIdAsync(Guid runId, CancellationToken ct = default)
        => await _db.RunRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
}