#nullable enable
namespace Tracto.Orchestrator.Domain.Admin;

using Tracto.Orchestrator.Domain.Runs;

/// <summary>
/// Lightweight read-model projection returned by GET /admin/jobs.
/// Contains only the fields required by the list view — no secret references.
///
/// Corresponds to the response schema:
///   { total, items: [{ runId, state, createdAt, needsAttention }] }
/// </summary>
public sealed record AdminJobSummary(
    Guid            RunId,
    RunState        State,
    DateTimeOffset  CreatedAt,
    bool            NeedsAttention
);

/// <summary>
/// Paged result wrapper returned by the admin job listing query.
/// </summary>
public sealed record AdminJobPage(
    int                         Total,
    IReadOnlyList<AdminJobSummary> Items
);
