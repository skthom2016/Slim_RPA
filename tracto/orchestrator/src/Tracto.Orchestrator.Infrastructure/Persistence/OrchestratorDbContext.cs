using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Tracto.Orchestrator.Domain.Artifacts;
using Tracto.Orchestrator.Domain.Callbacks;
using Tracto.Orchestrator.Domain.Journal;
using Tracto.Orchestrator.Domain.Leases;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Domain.Security;
using Tracto.Orchestrator.Domain.Telemetry;
using Tracto.Orchestrator.Domain.Workers;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Tracto.Orchestrator.Infrastructure.Persistence;

// Infrastructure-only DTO used for JSON round-tripping WorkerCapability.
// Declared after the file-scoped namespace so CS8956 is not triggered.
file sealed record WorkerCapabilityDto(
    string[]  ActionNames,
    string    RuntimeType,
    string?   CapabilityDigest = null);

// Static helpers called from EF Core expression-tree lambdas.
// Statement bodies and pattern-matching operators are not allowed inside expression trees,
// so the logic is factored out into plain static methods (CS0834 / CS8122 / CS9175 fix).
file static class CapabilityConversion
{
    internal static string? ToJson(WorkerCapability? cap)
    {
        if (cap is null) return null;
        var dto = new WorkerCapabilityDto(cap.ActionNames.ToArray(), cap.RuntimeType, cap.CapabilityDigest);
        return JsonSerializer.Serialize(dto, (JsonSerializerOptions?)null);
    }

    internal static WorkerCapability? FromJson(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        var dto = JsonSerializer.Deserialize<WorkerCapabilityDto>(json, (JsonSerializerOptions?)null);
        if (dto is null) return null;
        return new WorkerCapability(dto.ActionNames, dto.RuntimeType, dto.CapabilityDigest);
    }
}

/// <summary>
/// EF Core DbContext for the Tracto Orchestrator.
/// All state writes go through this context inside EF Core transactions (invariant 5).
/// No Windows-only P/Invoke APIs are referenced here; the context runs cross-platform.
/// </summary>
public sealed class OrchestratorDbContext : DbContext
{
    public OrchestratorDbContext(DbContextOptions<OrchestratorDbContext> options)
        : base(options) { }

    public DbSet<RunRecord> RunRecords => Set<RunRecord>();
    public DbSet<WorkerRecord> WorkerRecords => Set<WorkerRecord>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<ArtifactRecord> ArtifactRecords => Set<ArtifactRecord>();
    public DbSet<ArtifactContent> ArtifactContents => Set<ArtifactContent>();
    public DbSet<ScheduleRecord> ScheduleRecords => Set<ScheduleRecord>();
    public DbSet<LeaseRecord> LeaseRecords => Set<LeaseRecord>();

    // OC-06: Final Result Processing
    public DbSet<FinalResultRecord> FinalResultRecords => Set<FinalResultRecord>();

    // OC-07: Async Callback Intake
    public DbSet<CallbackRecord> CallbackRecords => Set<CallbackRecord>();

    // OC-17: Security Controls
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();

    // OC-08: Continuation Mapping and Run B Creation
    public DbSet<ContinuationMapping>   ContinuationMappings   => Set<ContinuationMapping>();
    public DbSet<ContinuationRunRecord> ContinuationRunRecords => Set<ContinuationRunRecord>();

    // OC-14: Telemetry and Observability
    public DbSet<TelemetryEvent> TelemetryEvents => Set<TelemetryEvent>();

    // OC-12: UNKNOWN Runs and Operator Resolution
    public DbSet<ResolutionRecord> ResolutionRecords => Set<ResolutionRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<RunRecord>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Id).ValueGeneratedNever();

            entity.Property(r => r.WorkflowId)
                  .IsRequired()
                  .HasMaxLength(256);

            entity.Property(r => r.State)
                  .HasConversion<string>()   // store as text for readability
                  .IsRequired();

            entity.Property(r => r.AttemptId).IsRequired();

            entity.Property(r => r.CreatedAt).IsRequired();
            entity.Property(r => r.UpdatedAt);

            entity.Property(r => r.NeedsAttention).IsRequired();
            entity.Property(r => r.UnknownReason).HasMaxLength(2048);
            entity.Property(r => r.WorkerId).HasMaxLength(256);

            // ── OC-02: Job Intake fields ──────────────────────────────────────
            entity.Property(r => r.CorrelationId).IsRequired();

            entity.Property(r => r.ScriptArtifactId)
                  .HasMaxLength(256);

            entity.Property(r => r.ScriptVersion)
                  .HasMaxLength(128);

            entity.Property(r => r.SchemaVersion)
                  .HasMaxLength(32);

            // InputRefs and SecretRefs are string[] stored as JSON text.
            var stringArrayComparer = new ValueComparer<string[]>(
                (a, b) => a != null && b != null && a.SequenceEqual(b),
                c => c == null ? 0 : c.Aggregate(0, (h, v) => HashCode.Combine(h, v.GetHashCode())),
                c => c == null ? new string[0] : c.ToArray());

            var stringArrayConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion
                .ValueConverter<string[], string>(
                    arr => JsonSerializer.Serialize(arr, (JsonSerializerOptions?)null),
                    json => JsonSerializer.Deserialize<string[]>(json, (JsonSerializerOptions?)null) ?? new string[0]);

            entity.Property(r => r.InputRefs)
                  .HasColumnName("InputRefsJson")
                  .HasColumnType("TEXT")
                  .HasConversion(stringArrayConverter)
                  .Metadata
                  .SetValueComparer(stringArrayComparer);

            entity.Property(r => r.SecretRefs)
                  .HasColumnName("SecretRefsJson")
                  .HasColumnType("TEXT")
                  .HasConversion(stringArrayConverter)
                  .Metadata
                  .SetValueComparer(stringArrayComparer);

            entity.Property(r => r.Deadline);
            entity.Property(r => r.EventId).HasMaxLength(512);
            entity.Property(r => r.TriggerType)
                  .HasConversion<string>()
                  .IsRequired();
            entity.Property(r => r.BusinessJobId).HasMaxLength(256);

            // ── OC-03: Queue Management fields ───────────────────────────────
            entity.Property(r => r.InteractiveSessionId).HasMaxLength(256);

            // Index for fast queue-depth queries.
            entity.HasIndex(r => r.State).HasDatabaseName("IX_RunRecords_State");

            // Composite index for RestartRecoveryService.FindByWorkerAndStateAsync (M1 fix).
            // Prevents a full scan of Running rows when filtering by WorkerId during recovery.
            entity.HasIndex(r => new { r.WorkerId, r.State })
                  .HasDatabaseName("IX_RunRecords_WorkerId_State");

            // Index for EventId dedup lookups (OC-02).
            entity.HasIndex(r => new { r.EventId, r.CreatedAt })
                  .HasDatabaseName("IX_RunRecords_EventId_CreatedAt");
        });

        // ── OC-11: Artifact Registry ──────────────────────────────────────────

        modelBuilder.Entity<ArtifactRecord>(entity =>
        {
            entity.HasKey(a => new { a.ArtifactId, a.Version });

            entity.Property(a => a.ArtifactId)
                  .IsRequired()
                  .HasMaxLength(256);

            entity.Property(a => a.Version)
                  .IsRequired()
                  .HasMaxLength(128);

            entity.Property(a => a.SchemaVersion)
                  .IsRequired()
                  .HasMaxLength(32);

            entity.Property(a => a.ContentHash)
                  .IsRequired()
                  .HasMaxLength(128);

            entity.Property(a => a.StoredAt).IsRequired();
            entity.Property(a => a.IsDeleted).IsRequired();
            entity.Property(a => a.SignatureValid).IsRequired();

            entity.Property(a => a.SelectorContent)
                  .HasMaxLength(65536);     // stored opaque; generous limit

            // ── OC-03: Capability matching fields ────────────────────────────
            entity.Property(a => a.RuntimeType)
                  .HasMaxLength(128)
                  .HasDefaultValue(string.Empty);

            // RequiredCapabilities stored as JSON text (same pattern as InputRefs/SecretRefs).
            var reqCapComparer = new ValueComparer<string[]>(
                (a, b) => a != null && b != null && a.SequenceEqual(b),
                c => c == null ? 0 : c.Aggregate(0, (h, v) => HashCode.Combine(h, v.GetHashCode())),
                c => c == null ? new string[0] : c.ToArray());

            var reqCapConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion
                .ValueConverter<string[], string>(
                    arr => JsonSerializer.Serialize(arr, (JsonSerializerOptions?)null),
                    json => JsonSerializer.Deserialize<string[]>(json, (JsonSerializerOptions?)null) ?? new string[0]);

            entity.Property(a => a.RequiredCapabilities)
                  .HasColumnName("RequiredCapabilitiesJson")
                  .HasColumnType("TEXT")
                  .HasConversion(reqCapConverter)
                  .Metadata
                  .SetValueComparer(reqCapComparer);

            // Fast look-up for not-deleted versions of an artifact.
            entity.HasIndex(a => new { a.ArtifactId, a.IsDeleted })
                  .HasDatabaseName("IX_ArtifactRecords_ArtifactId_IsDeleted");
        });

        modelBuilder.Entity<ArtifactContent>(entity =>
        {
            entity.HasKey(c => new { c.ArtifactId, c.Version });

            entity.Property(c => c.ArtifactId)
                  .IsRequired()
                  .HasMaxLength(256);

            entity.Property(c => c.Version)
                  .IsRequired()
                  .HasMaxLength(128);

            entity.Property(c => c.Payload)
                  .IsRequired();
        });

        modelBuilder.Entity<WorkerRecord>(entity =>
        {
            entity.HasKey(w => w.WorkerId);
            entity.Property(w => w.WorkerId)
                  .IsRequired()
                  .HasMaxLength(256)
                  .ValueGeneratedNever();

            entity.Property(w => w.RuntimeType)
                  .IsRequired()
                  .HasMaxLength(128);

            entity.Property(w => w.State)
                  .HasConversion<string>()
                  .IsRequired();

            entity.Property(w => w.RegisteredAt).IsRequired();
            entity.Property(w => w.LastHeartbeatAt).IsRequired();
            entity.Property(w => w.AbsenceThresholdSeconds).IsRequired();
            entity.Property(w => w.IsOnline).IsRequired();

            // OC-01: LastRegisteredAt — nullable, updated on every registration call.
            entity.Property(w => w.LastRegisteredAt);

            // OC-01: Capabilities stored as JSON text in a single column.
            // The value comparer ensures EF Core detects changes to the object graph.
            var capabilityComparer = new ValueComparer<WorkerCapability?>(
                (a, b) => a == b,
                c => c == null ? 0 : c.GetHashCode(),
                c => c == null
                     ? null
                     : new WorkerCapability(c.ActionNames, c.RuntimeType, c.CapabilityDigest));

            // ValueConverter delegates call static methods so expression-tree lambdas stay
            // simple (CS0834 / CS8122 / CS9175 — no statement bodies or pattern-matching).
            var capabilityConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<WorkerCapability?, string?>(
                cap => CapabilityConversion.ToJson(cap),
                json => CapabilityConversion.FromJson(json));

            entity.Property(w => w.Capabilities)
                  .HasColumnName("CapabilitiesJson")
                  .HasColumnType("TEXT")
                  .HasConversion(capabilityConverter)
                  .Metadata
                  .SetValueComparer(capabilityComparer);

            entity.HasIndex(w => w.IsOnline).HasDatabaseName("IX_WorkerRecords_IsOnline");
        });

        modelBuilder.Entity<JournalEntry>(entity =>
        {
            entity.HasKey(j => j.Id);
            entity.Property(j => j.Id).ValueGeneratedNever();

            entity.Property(j => j.RunId).IsRequired();
            entity.Property(j => j.AttemptId);

            entity.Property(j => j.EventType)
                  .IsRequired()
                  .HasMaxLength(128);

            entity.Property(j => j.OccurredAt).IsRequired();
            entity.Property(j => j.Payload).HasMaxLength(8192);

            entity.HasIndex(j => j.RunId).HasDatabaseName("IX_JournalEntries_RunId");
            entity.HasIndex(j => j.OccurredAt).HasDatabaseName("IX_JournalEntries_OccurredAt");
        });

        // ── OC-03: Lease Registry ─────────────────────────────────────────────

        modelBuilder.Entity<LeaseRecord>(entity =>
        {
            entity.HasKey(l => l.LeaseId);
            entity.Property(l => l.LeaseId).ValueGeneratedNever();

            entity.Property(l => l.RunId).IsRequired();
            entity.Property(l => l.AttemptId).IsRequired();

            entity.Property(l => l.WorkerId)
                  .IsRequired()
                  .HasMaxLength(256);

            entity.Property(l => l.ArtifactId)
                  .IsRequired()
                  .HasMaxLength(256);

            entity.Property(l => l.ArtifactVersion)
                  .IsRequired()
                  .HasMaxLength(128);

            entity.Property(l => l.ArtifactHash)
                  .IsRequired()
                  .HasMaxLength(128);

            entity.Property(l => l.OfferedAt).IsRequired();
            entity.Property(l => l.ExpiresAt).IsRequired();
            entity.Property(l => l.AcceptedAt);

            entity.Property(l => l.State)
                  .HasConversion<string>()
                  .IsRequired();

            entity.Property(l => l.InteractiveSessionId)
                  .HasMaxLength(256);

            // ── OC-05: Heartbeat fields ───────────────────────────────────────
            entity.Property(l => l.LastHeartbeatAt);
            entity.Property(l => l.LastSeqNo).IsRequired();
            entity.Property(l => l.CancellationRequested).IsRequired();

            // Fast lookup: all non-expired leases for a run.
            entity.HasIndex(l => new { l.RunId, l.State })
                  .HasDatabaseName("IX_LeaseRecords_RunId_State");

            // Fast lookup: leases held by a worker (enforces single-lease invariant).
            entity.HasIndex(l => new { l.WorkerId, l.State })
                  .HasDatabaseName("IX_LeaseRecords_WorkerId_State");

            // Fast lookup: interactive-session exclusivity.
            entity.HasIndex(l => l.InteractiveSessionId)
                  .HasDatabaseName("IX_LeaseRecords_InteractiveSessionId");
        });

        // ── OC-02: Schedule Registry ──────────────────────────────────────────

        modelBuilder.Entity<ScheduleRecord>(entity =>
        {
            entity.HasKey(s => s.ScheduleId);
            entity.Property(s => s.ScheduleId).ValueGeneratedNever();

            entity.Property(s => s.CronExpression)
                  .IsRequired()
                  .HasMaxLength(128);

            entity.Property(s => s.ArtifactId)
                  .IsRequired()
                  .HasMaxLength(256);

            entity.Property(s => s.Version)
                  .IsRequired()
                  .HasMaxLength(128);

            entity.Property(s => s.Enabled).IsRequired();
            entity.Property(s => s.CreatedAt).IsRequired();
            entity.Property(s => s.UpdatedAt);

            entity.HasIndex(s => s.Enabled).HasDatabaseName("IX_ScheduleRecords_Enabled");
        });

        // ── OC-06: Final Result Processing ────────────────────────────────────

        modelBuilder.Entity<FinalResultRecord>(entity =>
        {
            entity.HasKey(f => f.Id);
            entity.Property(f => f.Id).ValueGeneratedNever();

            entity.Property(f => f.RunId).IsRequired();
            entity.Property(f => f.AttemptId).IsRequired();

            entity.Property(f => f.TerminalState)
                  .HasConversion<string>()
                  .IsRequired();

            entity.Property(f => f.NeedsAttention).IsRequired();
            entity.Property(f => f.UnknownReason).HasMaxLength(2048);
            entity.Property(f => f.ErrorCategory).HasMaxLength(256);
            entity.Property(f => f.ErrorMessage).HasMaxLength(4096);
            entity.Property(f => f.BusinessOutcome).HasMaxLength(512);
            entity.Property(f => f.BusinessJobId).HasMaxLength(256);
            entity.Property(f => f.CompletedAt).IsRequired();

            // EvidenceRefs stored as JSON text (same pattern as InputRefs).
            var evidenceRefsComparer = new ValueComparer<string[]>(
                (a, b) => a != null && b != null && a.SequenceEqual(b),
                c => c == null ? 0 : c.Aggregate(0, (h, v) => HashCode.Combine(h, v.GetHashCode())),
                c => c == null ? new string[0] : c.ToArray());

            var evidenceRefsConverter = new ValueConverter<string[], string>(
                arr => System.Text.Json.JsonSerializer.Serialize(arr, (System.Text.Json.JsonSerializerOptions?)null),
                json => System.Text.Json.JsonSerializer.Deserialize<string[]>(json, (System.Text.Json.JsonSerializerOptions?)null) ?? new string[0]);

            entity.Property(f => f.EvidenceRefs)
                  .HasColumnName("EvidenceRefsJson")
                  .HasColumnType("TEXT")
                  .HasConversion(evidenceRefsConverter)
                  .Metadata
                  .SetValueComparer(evidenceRefsComparer);

            // Each RunId has at most one FinalResultRecord.
            entity.HasIndex(f => f.RunId)
                  .IsUnique()
                  .HasDatabaseName("IX_FinalResultRecords_RunId");
        });

        // ── OC-07: Callback Records ───────────────────────────────────────────────

        modelBuilder.Entity<CallbackRecord>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Id).ValueGeneratedNever();

            entity.Property(c => c.EventId)
                  .IsRequired()
                  .HasMaxLength(512);

            entity.Property(c => c.CorrelationId).IsRequired();

            entity.Property(c => c.KeyId)
                  .IsRequired()
                  .HasMaxLength(128);    // KeyId is an opaque identifier; key material is never stored.

            entity.Property(c => c.ReceivedAt).IsRequired();

            entity.Property(c => c.PayloadJson)
                  .IsRequired()
                  .HasMaxLength(65536);  // Generous limit; payload is forwarded opaquely to OC-08.

            entity.Property(c => c.Processed).IsRequired();

            // Fast deduplication lookup — every ingestion call scans this index first.
            entity.HasIndex(c => c.EventId)
                  .IsUnique()
                  .HasDatabaseName("IX_CallbackRecords_EventId");

            // Secondary index: find unprocessed callbacks for any pending continuation work.
            entity.HasIndex(c => new { c.CorrelationId, c.Processed })
                  .HasDatabaseName("IX_CallbackRecords_CorrelationId_Processed");
        });

        // ── OC-17: Security Events ─────────────────────────────────────────────

        modelBuilder.Entity<SecurityEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();

            entity.Property(e => e.EventType)
                  .IsRequired()
                  .HasMaxLength(128);   // stored as plain text string — no enum conversion needed.

            entity.Property(e => e.SourceIp)
                  .HasMaxLength(64);      // IPv6 max = 39 chars; 64 gives room for zone IDs.

            entity.Property(e => e.OccurredAt).IsRequired();

            entity.Property(e => e.Details)
                  .IsRequired()
                  .HasMaxLength(4096);    // Generous limit; details are pre-sanitised.

            // Fast look-up for admin observability (newest-first queries).
            entity.HasIndex(e => e.OccurredAt)
                  .HasDatabaseName("IX_SecurityEvents_OccurredAt");

            // Fast look-up by event type (admin filter queries).
            entity.HasIndex(e => e.EventType)
                  .HasDatabaseName("IX_SecurityEvents_EventType");
        });

        // ── OC-08: Continuation Mapping ───────────────────────────────────────

        modelBuilder.Entity<ContinuationMapping>(entity =>
        {
            entity.HasKey(m => m.MappingId);
            entity.Property(m => m.MappingId).ValueGeneratedNever();

            entity.Property(m => m.TriggerRunId);   // nullable Guid — null = global mapping

            entity.Property(m => m.TriggerState)
                  .HasConversion<string>()           // stored as enum name text
                  .IsRequired();

            entity.Property(m => m.TargetArtifactId)
                  .IsRequired()
                  .HasMaxLength(256);

            entity.Property(m => m.TargetArtifactVersion)
                  .IsRequired()
                  .HasMaxLength(128);

            entity.Property(m => m.InputTemplate)
                  .HasMaxLength(65536);              // opaque JSON template; generous limit

            entity.Property(m => m.CreatedAt).IsRequired();

            // Optimistic-concurrency version counter — incremented on every update.
            entity.Property(m => m.Version).IsRequired();

            // Fast lookup: find the mapping for a specific (run, state) pair.
            entity.HasIndex(m => new { m.TriggerRunId, m.TriggerState })
                  .HasDatabaseName("IX_ContinuationMappings_TriggerRunId_TriggerState");

            // Fast lookup: find global (state-only) mappings when TriggerRunId is null.
            entity.HasIndex(m => m.TriggerState)
                  .HasDatabaseName("IX_ContinuationMappings_TriggerState");
        });

        // ── OC-08: Continuation Run Dedup Records ─────────────────────────────
        //
        // The unique index on (SourceRunId, TriggerState) is the authoritative dedup guard:
        // inserting a duplicate row raises a DB constraint violation, which is caught in
        // CreateContinuationRunCommandHandler and treated as an idempotent 200 (no second Run B).

        modelBuilder.Entity<ContinuationRunRecord>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Id).ValueGeneratedNever();

            entity.Property(r => r.SourceRunId).IsRequired();

            entity.Property(r => r.TriggerState)
                  .HasConversion<string>()
                  .IsRequired();

            entity.Property(r => r.RunBId).IsRequired();
            entity.Property(r => r.MappingId).IsRequired();

            entity.Property(r => r.MappingVersionSnapshot).IsRequired();

            entity.Property(r => r.CreatedAt).IsRequired();

            // THE dedup guard: only one Run B per (SourceRunId, TriggerState).
            entity.HasIndex(r => new { r.SourceRunId, r.TriggerState })
                  .IsUnique()
                  .HasDatabaseName("IX_ContinuationRunRecords_SourceRunId_TriggerState");

            // Fast lookup: find the Run B for a specific SourceRunId.
            entity.HasIndex(r => r.SourceRunId)
                  .HasDatabaseName("IX_ContinuationRunRecords_SourceRunId");
        });

        // ── OC-12: Resolution Records ─────────────────────────────────────────
        //
        // Append-only audit table: one row per operator resolution on an UNKNOWN run.
        // The original RunRecord is NEVER modified — it stays UNKNOWN with NeedsAttention=true.
        // NewRunId is null for InvestigatedNoRerun; non-null for ApprovedNewRun.

        modelBuilder.Entity<ResolutionRecord>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Id).ValueGeneratedNever();

            entity.Property(r => r.RunId).IsRequired();

            entity.Property(r => r.Type)
                  .HasConversion<string>()   // "InvestigatedNoRerun" | "ApprovedNewRun"
                  .IsRequired();

            entity.Property(r => r.Justification)
                  .IsRequired()
                  .HasMaxLength(4096);

            entity.Property(r => r.OperatorId)
                  .IsRequired()
                  .HasMaxLength(256);

            entity.Property(r => r.ResolvedAt).IsRequired();

            entity.Property(r => r.NewRunId);  // nullable Guid — null for InvestigatedNoRerun

            // Fast lookup: all resolutions for a given UNKNOWN run, ordered by ResolvedAt.
            entity.HasIndex(r => r.RunId)
                  .HasDatabaseName("IX_ResolutionRecords_RunId");
        });

        // ── OC-14: Telemetry Events ───────────────────────────────────────────

        modelBuilder.Entity<TelemetryEvent>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Id).ValueGeneratedNever();

            entity.Property(t => t.RunId).IsRequired();

            entity.Property(t => t.WorkerId)
                  .HasMaxLength(256);

            entity.Property(t => t.LeaseId);
            entity.Property(t => t.AttemptId);

            entity.Property(t => t.EventType)
                  .IsRequired()
                  .HasMaxLength(128);

            entity.Property(t => t.OccurredAt).IsRequired();

            // Metadata is an opaque JSON string; no size cap beyond SQLite TEXT max.
            entity.Property(t => t.Metadata)
                  .HasColumnType("TEXT");

            // Primary read pattern: "give me all events for run X, ordered by time".
            entity.HasIndex(t => new { t.RunId, t.OccurredAt })
                  .HasDatabaseName("IX_TelemetryEvents_RunId_OccurredAt");

            // Secondary: filter by event type across all runs (admin dashboards).
            entity.HasIndex(t => t.EventType)
                  .HasDatabaseName("IX_TelemetryEvents_EventType");
        });
    }
}
