#nullable enable
using Microsoft.AspNetCore.Mvc;
using Tracto.Orchestrator.Application.Jobs;

namespace Tracto.Orchestrator.Api.Schedules;

// ── Request / Response DTOs ───────────────────────────────────────────────────

/// <summary>
/// POST /schedules request body (OC-02 Schedule Registry).
/// </summary>
/// <param name="CronExpression">Standard five-field UNIX cron expression.</param>
/// <param name="ArtifactId">Artifact to execute when the schedule fires.</param>
/// <param name="Version">Pinned artifact version.</param>
/// <param name="Enabled">Whether the schedule should fire immediately. Defaults to true.</param>
public sealed record CreateScheduleRequest(
    string CronExpression,
    string ArtifactId,
    string Version,
    bool   Enabled = true);

/// <summary>Successful response from POST /schedules.</summary>
public sealed record ScheduleResponse(Guid ScheduleId, string CronExpression, string ArtifactId, string Version, bool Enabled);

// ── Endpoint wiring ───────────────────────────────────────────────────────────

/// <summary>
/// OC-02: Schedule Registry endpoints.
///   POST /schedules — create a new cron schedule entry.
/// </summary>
public static class ScheduleEndpoints
{
    /// <summary>
    /// Maps all schedule-related endpoints onto <paramref name="app"/>.
    /// Call this from Program.cs after the middleware pipeline is configured.
    /// </summary>
    public static void MapScheduleEndpoints(this WebApplication app)
    {
        // ── POST /schedules ───────────────────────────────────────────────────
        // Creates a new ScheduleRecord. Returns 200 {scheduleId} on success.
        // 400 when required fields are missing.
        app.MapPost("/schedules", async (
            [FromBody]   CreateScheduleRequest   request,
            [FromServices] CreateScheduleCommandHandler handler,
            CancellationToken ct) =>
        {
            var command = new CreateScheduleCommand(
                CronExpression: request.CronExpression,
                ArtifactId:     request.ArtifactId,
                Version:        request.Version,
                Enabled:        request.Enabled);

            var result = await handler.HandleAsync(command, ct);

            if (result.IsFailure)
            {
                return Results.Problem(
                    title:      result.Error.Code,
                    detail:     result.Error.Message,
                    statusCode: StatusCodes.Status400BadRequest,
                    type:       $"https://tracto.io/errors/{result.Error.Code}");
            }

            var v = result.Value;
            return Results.Ok(new ScheduleResponse(
                v.ScheduleId,
                v.CronExpression,
                v.ArtifactId,
                v.Version,
                v.Enabled));
        })
        .WithName("CreateSchedule")
        .WithTags("Schedules")
        .Accepts<CreateScheduleRequest>("application/json")
        .Produces<ScheduleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
