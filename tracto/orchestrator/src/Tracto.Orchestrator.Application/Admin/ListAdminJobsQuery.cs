#nullable enable
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Runs;

namespace Tracto.Orchestrator.Application.Admin;

// ── Query ──────────────────────────────────────────────────────────────────────

/// <summary>
/// Returns a filtered, paged list of runs for the admin job listing endpoint (OC-13).
/// GET /admin/jobs?status=&amp;from=&amp;to=&amp;page=&amp;pageSize=
/// All parameters are optional; omitting them returns all runs (subject to pagination).
/// </summary>
public sealed record ListAdminJobsQuery(
    RunState?       Status   = null,
    DateTimeOffset? From     = null,
    DateTimeOffset? To       = null,
    int             Page     = 1,
    int             PageSize = 50);

// ── Handler ────────────────────────────────────────────────────────────────────

/// <summary>
/// Handles <see cref="ListAdminJobsQuery"/> for OC-13.
/// Delegates the actual query to <see cref="IAdminJobRepository"/> which pushes all
/// filtering and paging down to the database.
/// </summary>
public sealed class ListAdminJobsQueryHandler
{
    private const int MaxPageSize = 200;
    private const int MinPageSize = 1;

    private readonly IAdminJobRepository _adminRepo;

    public ListAdminJobsQueryHandler(IAdminJobRepository adminRepo)
    {
        _adminRepo = adminRepo;
    }

    public async Task<Result<AdminJobPage, Error>> HandleAsync(
        ListAdminJobsQuery query,
        CancellationToken  cancellationToken = default)
    {
        // Clamp pagination parameters so a caller cannot request an unreasonable result set.
        var page     = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, MinPageSize, MaxPageSize);

        // Validate date range when both are supplied.
        if (query.From.HasValue && query.To.HasValue && query.From > query.To)
            return Result<AdminJobPage, Error>.Fail(
                Error.Validation("'from' must not be later than 'to'."));

        var result = await _adminRepo.ListJobsAsync(
            state:             query.Status,
            from:              query.From,
            to:                query.To,
            page:              page,
            pageSize:          pageSize,
            cancellationToken: cancellationToken);

        return Result<AdminJobPage, Error>.Ok(result);
    }
}
