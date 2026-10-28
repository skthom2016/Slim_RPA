#nullable enable
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Common;

namespace Tracto.Orchestrator.Application.Admin;

// -- Query --------------------------------------------------------------------

/// <summary>
/// Returns the full admin detail view for a single run (OC-13).
/// GET /admin/jobs/{runId}
/// SecretRef values are never present in the response (domain invariant 4).
/// </summary>
public sealed record GetAdminJobDetailQuery(Guid RunId);

// -- Handler ------------------------------------------------------------------

/// <summary>Handles <see cref="GetAdminJobDetailQuery"/> for OC-13.</summary>
public sealed class GetAdminJobDetailQueryHandler
{
    private readonly IAdminJobRepository _adminRepo;

    public GetAdminJobDetailQueryHandler(IAdminJobRepository adminRepo)
    {
        _adminRepo = adminRepo;
    }

    public async Task<Result<AdminJobDetailView, Error>> HandleAsync(
        GetAdminJobDetailQuery query,
        CancellationToken      cancellationToken = default)
    {
        var detail = await _adminRepo.GetJobDetailAsync(query.RunId, cancellationToken);

        if (detail is null)
            return Result<AdminJobDetailView, Error>.Fail(
                Error.NotFound($"Run '{query.RunId}' was not found."));

        return Result<AdminJobDetailView, Error>.Ok(detail);
    }
}