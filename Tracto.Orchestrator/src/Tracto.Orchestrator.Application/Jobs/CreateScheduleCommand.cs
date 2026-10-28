#nullable enable
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Runs;

namespace Tracto.Orchestrator.Application.Jobs;

// ── Command ───────────────────────────────────────────────────────────────────

/// <summary>OC-02: Creates a new ScheduleRecord in the schedule registry.</summary>
public sealed record CreateScheduleCommand(
    string CronExpression,
    string ArtifactId,
    string Version,
    bool   Enabled);

/// <summary>Successful result returned by <see cref="CreateScheduleCommandHandler"/>.</summary>
public sealed record CreateScheduleResult(
    Guid   ScheduleId,
    string CronExpression,
    string ArtifactId,
    string Version,
    bool   Enabled);

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// Handles <see cref="CreateScheduleCommand"/>.
/// Validates the request, creates a <see cref="ScheduleRecord"/>, and persists it.
/// </summary>
public sealed class CreateScheduleCommandHandler
{
    private readonly IScheduleRepository _schedules;
    private readonly IUnitOfWork         _unitOfWork;
    private readonly IClock              _clock;

    public CreateScheduleCommandHandler(
        IScheduleRepository schedules,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _schedules  = schedules;
        _unitOfWork = unitOfWork;
        _clock      = clock;
    }

    public async Task<Result<CreateScheduleResult, Error>> HandleAsync(
        CreateScheduleCommand cmd,
        CancellationToken cancellationToken = default)
    {
        // ── Validation ────────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(cmd.CronExpression))
            return Result<CreateScheduleResult, Error>.Fail(
                Error.Validation("CronExpression is required."));

        if (string.IsNullOrWhiteSpace(cmd.ArtifactId))
            return Result<CreateScheduleResult, Error>.Fail(
                Error.Validation("ArtifactId is required."));

        if (string.IsNullOrWhiteSpace(cmd.Version))
            return Result<CreateScheduleResult, Error>.Fail(
                Error.Validation("Version is required."));

        var now        = _clock.UtcNow;
        var scheduleId = Guid.NewGuid();

        var schedule = new ScheduleRecord(
            scheduleId:     scheduleId,
            cronExpression: cmd.CronExpression,
            artifactId:     cmd.ArtifactId,
            version:        cmd.Version,
            enabled:        cmd.Enabled,
            createdAt:      now);

        await _unitOfWork.BeginAsync(cancellationToken);
        await _schedules.AddAsync(schedule, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);

        return Result<CreateScheduleResult, Error>.Ok(
            new CreateScheduleResult(
                ScheduleId:     scheduleId,
                CronExpression: cmd.CronExpression,
                ArtifactId:     cmd.ArtifactId,
                Version:        cmd.Version,
                Enabled:        cmd.Enabled));
    }
}
