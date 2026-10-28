#nullable enable
namespace Tracto.Orchestrator.Domain.Admin;

using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Runs;

/// <summary>
/// Value object capturing the filter parameters for the admin job listing endpoint.
///   GET /admin/jobs with optional query params: status, from, to, page, pageSize.
///
/// Domain invariants:
///   - Page must be >= 1.
///   - PageSize must be between 1 and 1000.
///   - When both From and To are supplied, From must not be later than To.
/// </summary>
public sealed record AdminJobFilter
{
    private AdminJobFilter() { }

    public RunState?       Status   { get; private init; }
    public DateTimeOffset? From     { get; private init; }
    public DateTimeOffset? To       { get; private init; }
    public int             Page     { get; private init; }
    public int             PageSize { get; private init; }

    /// <summary>Zero-based offset derived from Page / PageSize for EF Core Skip().</summary>
    public int Offset => (Page - 1) * PageSize;

    // ── Factory ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a validated <see cref="AdminJobFilter"/> or returns a domain error.
    /// </summary>
    public static Result<AdminJobFilter, Error> Create(
        RunState?       status,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int             page     = 1,
        int             pageSize = 25)
    {
        if (page < 1)
            return Error.Validation("Page must be >= 1.");
        if (pageSize < 1 || pageSize > 1000)
            return Error.Validation("PageSize must be between 1 and 1000.");
        if (from.HasValue && to.HasValue && from.Value > to.Value)
            return Error.Validation("'from' must not be later than 'to'.");

        return Result<AdminJobFilter, Error>.Ok(new AdminJobFilter
        {
            Status   = status,
            From     = from,
            To       = to,
            Page     = page,
            PageSize = pageSize
        });
    }
}
