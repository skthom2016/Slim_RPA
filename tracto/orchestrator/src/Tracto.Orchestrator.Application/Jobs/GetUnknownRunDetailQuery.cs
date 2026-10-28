#nullable enable
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Runs;

namespace Tracto.Orchestrator.Application.Jobs;

// ── Query ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Returns the full UNKNOWN-state detail for a run, including evidence references,
/// the last journal entry, and any operator resolution records already appended (OC-12).
/// </summary>
public sealed record GetUnknownRunDetailQuery(Guid RunId);

// ── Result DTOs ───────────────────────────────────────────────────────────────

/// <summary>Summary of one operator resolution attached to an UNKNOWN run.</summary>
public sealed record ResolutionDto(
    Guid Id,
    string Type,
    string Justification,
    string OperatorId,
    DateTimeOffset ResolvedAt,
    Guid? NewRunId);

/// <summary>
/// Full detail record returned for an UNKNOWN run (OC-12).
/// EvidenceRefs: journal event types that contributed to the UNKNOWN classification.
/// </summary>
public sealed record GetUnknownRunDetailResult(
    Guid RunId,
    string State,
    string? UnknownReason,
    bool NeedsAttention,
    string? LastJournalEntry,
    IReadOnlyList<string> EvidenceRefs,
    IReadOnlyList<ResolutionDto> Resolutions);

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// Handles <see cref="GetUnknownRunDetailQuery"/>:
///   1. Load run — 404 when not found.
///   2. 422 when run is not in UNKNOWN state.
///   3. Load journal entries, derive EvidenceRefs and the last entry's EventType.
///   4. Load existing resolution records.
///   5. Project and return.
/// </summary>
public sealed class GetUnknownRunDetailQueryHandler
{
    private readonly IRunRepository        _runRepo;
    private readonly IJournalRepository    _journalRepo;
    private readonly IResolutionRepository _resolutionRepo;

    public GetUnknownRunDetailQueryHandler(
        IRunRepository        runRepo,
        IJournalRepository    journalRepo,
        IResolutionRepository resolutionRepo)
    {
        _runRepo        = runRepo;
        _journalRepo    = journalRepo;
        _resolutionRepo = resolutionRepo;
    }

    public async Task<Result<GetUnknownRunDetailResult, Error>> HandleAsync(
        GetUnknownRunDetailQuery query,
        CancellationToken cancellationToken = default)
    {
        var run = await _runRepo.GetByIdAsync(query.RunId, cancellationToken);
        if (run is null)
            return Result<GetUnknownRunDetailResult, Error>.Fail(
                Error.NotFound($"Run '{query.RunId}' was not found."));

        if (run.State != RunState.Unknown)
            return Result<GetUnknownRunDetailResult, Error>.Fail(
                Error.Create("NOT_UNKNOWN_STATE",
                    $"Run '{query.RunId}' is in state {run.State.ToString().ToUpperInvariant()}, not UNKNOWN."));

        var entries = await _journalRepo.GetByRunIdAsync(query.RunId, cancellationToken);

        var lastEntry    = entries.Count > 0 ? entries[^1].EventType : null;
        var evidenceRefs = entries.Select(e => e.EventType).Distinct().ToList().AsReadOnly();

        var rawResolutions = await _resolutionRepo.GetByRunIdAsync(query.RunId, cancellationToken);
        // Map ResolutionType enum to the wire-format strings used by the POST /resolve endpoint
        // ("INVESTIGATED_NO_RERUN" / "APPROVED_NEW_RUN") so GET and POST roundtrip consistently.
        static string ToWireType(ResolutionType t) => t switch
        {
            ResolutionType.InvestigatedNoRerun => "INVESTIGATED_NO_RERUN",
            ResolutionType.ApprovedNewRun      => "APPROVED_NEW_RUN",
            _                                  => t.ToString().ToUpperInvariant(),
        };

        var resolutionDtos = rawResolutions
            .Select(r => new ResolutionDto(
                r.Id,
                ToWireType(r.Type),
                r.Justification,
                r.OperatorId,
                r.ResolvedAt,
                r.NewRunId))
            .ToList()
            .AsReadOnly();

        return Result<GetUnknownRunDetailResult, Error>.Ok(
            new GetUnknownRunDetailResult(
                RunId:           run.Id,
                State:           run.State.ToString().ToUpperInvariant(),
                UnknownReason:   run.UnknownReason,
                NeedsAttention:  run.NeedsAttention,
                LastJournalEntry: lastEntry,
                EvidenceRefs:    evidenceRefs,
                Resolutions:     resolutionDtos));
    }
}
