#nullable enable
namespace Tracto.Orchestrator.Domain.Runs;

/// <summary>
/// Represents a recurring schedule that the CronTriggerService uses to fire jobs automatically.
/// Domain layer — no EF annotations; mapping configured in OrchestratorDbContext.
/// </summary>
public sealed class ScheduleRecord
{
    // EF Core parameterless ctor.
    private ScheduleRecord() { }

    public ScheduleRecord(
        Guid scheduleId,
        string cronExpression,
        string artifactId,
        string version,
        bool enabled,
        DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
            throw new ArgumentException("CronExpression is required.", nameof(cronExpression));
        if (string.IsNullOrWhiteSpace(artifactId))
            throw new ArgumentException("ArtifactId is required.", nameof(artifactId));
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Version is required.", nameof(version));

        ScheduleId = scheduleId;
        CronExpression = cronExpression;
        ArtifactId = artifactId;
        Version = version;
        Enabled = enabled;
        CreatedAt = createdAt;
    }

    /// <summary>Primary key.</summary>
    public Guid ScheduleId { get; private set; }

    /// <summary>Standard UNIX cron expression (five fields).</summary>
    public string CronExpression { get; private set; } = string.Empty;

    /// <summary>Artifact to execute when the schedule fires.</summary>
    public string ArtifactId { get; private set; } = string.Empty;

    /// <summary>Pinned version of the artifact.</summary>
    public string Version { get; private set; } = string.Empty;

    /// <summary>When false the CronTriggerService skips this schedule without deleting it.</summary>
    public bool Enabled { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    // ── Mutators ───────────────────────────────────────────────────────────────

    /// <summary>Enables this schedule so the CronTriggerService will fire it.</summary>
    public void Enable(DateTimeOffset now)
    {
        Enabled = true;
        UpdatedAt = now;
    }

    /// <summary>Disables this schedule without deleting it.</summary>
    public void Disable(DateTimeOffset now)
    {
        Enabled = false;
        UpdatedAt = now;
    }

    /// <summary>Updates the cron expression. Empty or whitespace-only strings are rejected.</summary>
    public void UpdateCronExpression(string cronExpression, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
            throw new ArgumentException("CronExpression cannot be empty.", nameof(cronExpression));
        CronExpression = cronExpression;
        UpdatedAt = now;
    }
}
