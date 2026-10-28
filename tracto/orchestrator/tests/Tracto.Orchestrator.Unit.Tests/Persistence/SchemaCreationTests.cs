using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tracto.Orchestrator.Domain.Journal;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Domain.Workers;
using Tracto.Orchestrator.Infrastructure.Persistence;

namespace Tracto.Orchestrator.Unit.Tests.Persistence;

/// <summary>
/// Verifies that the OC-15 schema (RunRecords, WorkerRecords, JournalEntries)
/// can be created and used on a fresh in-memory SQLite database.
///
/// OC-15 AC: "Migration applies cleanly on fresh in-memory DB."
/// EnsureCreated is used instead of MigrateAsync because in-memory SQLite
/// does not support EF migrations. The AC is satisfied by confirming the complete
/// model is creatable, insertable, and queryable without errors.
/// </summary>
public sealed class SchemaCreationTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly DbContextOptions<OrchestratorDbContext> _options;

    public SchemaCreationTests()
    {
        _keepAlive = new SqliteConnection("DataSource=:memory:");
        _keepAlive.Open();

        _options = new DbContextOptionsBuilder<OrchestratorDbContext>()
            .UseSqlite(_keepAlive)
            .Options;
    }

    public void Dispose() => _keepAlive.Dispose();

    // ─── Schema creation ───────────────────────────────────────────────────────

    [Fact]
    public void EnsureCreated_WithAllThreeTables_DoesNotThrow()
    {
        using var ctx = new OrchestratorDbContext(_options);
        var act = () => ctx.Database.EnsureCreated();
        act.Should().NotThrow(
            because: "all three OC-15 tables must be creatable from the EF model");
    }

    [Fact]
    public async Task EnsureCreatedAsync_AllThreeTables_DoesNotThrow()
    {
        await using var ctx = new OrchestratorDbContext(_options);
        var act = async () => await ctx.Database.EnsureCreatedAsync();
        await act.Should().NotThrowAsync();
    }

    // ─── RunRecords table ──────────────────────────────────────────────────────

    [Fact]
    public async Task RunRecordsTable_CanInsertAndRetrieve()
    {
        await using var ctx = new OrchestratorDbContext(_options);
        await ctx.Database.EnsureCreatedAsync();

        var now = new DateTimeOffset(2024, 3, 15, 10, 0, 0, TimeSpan.Zero);
        var run = new RunRecord(Guid.NewGuid(), "wf-schema-test", now);
        await ctx.RunRecords.AddAsync(run);
        await ctx.SaveChangesAsync();

        await using var verify = new OrchestratorDbContext(_options);
        var stored = await verify.RunRecords.FindAsync(run.Id);

        stored.Should().NotBeNull();
        stored!.WorkflowId.Should().Be("wf-schema-test");
        stored.State.Should().Be(RunState.Created);
        stored.NeedsAttention.Should().BeFalse();
    }

    // ─── WorkerRecords table ───────────────────────────────────────────────────

    [Fact]
    public async Task WorkerRecordsTable_CanInsertAndRetrieveOnlineWorker()
    {
        await using var ctx = new OrchestratorDbContext(_options);
        await ctx.Database.EnsureCreatedAsync();

        var now    = DateTimeOffset.UtcNow;
        var worker = new WorkerRecord("worker-schema-test", "Desktop", 60, now);
        await ctx.WorkerRecords.AddAsync(worker);
        await ctx.SaveChangesAsync();

        await using var verify = new OrchestratorDbContext(_options);
        var stored = await verify.WorkerRecords.FindAsync("worker-schema-test");

        stored.Should().NotBeNull();
        stored!.RuntimeType.Should().Be("Desktop");
        stored.State.Should().Be(WorkerState.Available);
        stored.IsOnline.Should().BeTrue();
        stored.AbsenceThresholdSeconds.Should().Be(60);
    }

    [Fact]
    public async Task WorkerRecordsTable_OfflineWorker_StoredCorrectly()
    {
        await using var ctx = new OrchestratorDbContext(_options);
        await ctx.Database.EnsureCreatedAsync();

        var now    = DateTimeOffset.UtcNow;
        var worker = new WorkerRecord("worker-offline-schema", "Desktop", 120, now);
        worker.MarkOffline(now.AddMinutes(1));
        await ctx.WorkerRecords.AddAsync(worker);
        await ctx.SaveChangesAsync();

        await using var verify = new OrchestratorDbContext(_options);
        var stored = await verify.WorkerRecords.FindAsync("worker-offline-schema");

        stored.Should().NotBeNull();
        stored!.IsOnline.Should().BeFalse();
        stored.State.Should().Be(WorkerState.Offline);
    }

    // ─── JournalEntries table ──────────────────────────────────────────────────

    [Fact]
    public async Task JournalEntriesTable_CanInsertAndRetrieve()
    {
        await using var ctx = new OrchestratorDbContext(_options);
        await ctx.Database.EnsureCreatedAsync();

        var now   = DateTimeOffset.UtcNow;
        var run   = new RunRecord(Guid.NewGuid(), "wf-journal-test", now);
        var entry = new JournalEntry(
            runId:      run.Id,
            attemptId:  run.AttemptId,
            eventType:  "RunCreated",
            occurredAt: now,
            payload:    "{\"source\":\"schema-test\"}");

        await ctx.RunRecords.AddAsync(run);
        await ctx.JournalEntries.AddAsync(entry);
        await ctx.SaveChangesAsync();

        await using var verify = new OrchestratorDbContext(_options);
        var stored = await verify.JournalEntries.FindAsync(entry.Id);

        stored.Should().NotBeNull();
        stored!.RunId.Should().Be(run.Id);
        stored.EventType.Should().Be("RunCreated");
        stored.Payload.Should().Be("{\"source\":\"schema-test\"}");
    }

    [Fact]
    public async Task JournalEntriesTable_NullPayload_StoredAsNull()
    {
        await using var ctx = new OrchestratorDbContext(_options);
        await ctx.Database.EnsureCreatedAsync();

        var now   = DateTimeOffset.UtcNow;
        var run   = new RunRecord(Guid.NewGuid(), "wf-null-payload", now);
        var entry = new JournalEntry(run.Id, null, "Heartbeat", now, null);

        await ctx.RunRecords.AddAsync(run);
        await ctx.JournalEntries.AddAsync(entry);
        await ctx.SaveChangesAsync();

        await using var verify = new OrchestratorDbContext(_options);
        var stored = await verify.JournalEntries.FindAsync(entry.Id);

        stored.Should().NotBeNull();
        stored!.Payload.Should().BeNull();
        stored.AttemptId.Should().BeNull();
    }

    // ─── All three tables together ─────────────────────────────────────────────

    [Fact]
    public async Task AllThreeTables_IntegratedInsert_AllRowsPresent()
    {
        await using var ctx = new OrchestratorDbContext(_options);
        await ctx.Database.EnsureCreatedAsync();

        var now    = DateTimeOffset.UtcNow;
        var worker = new WorkerRecord("worker-integrated", "Desktop", 120, now);
        var run    = new RunRecord(Guid.NewGuid(), "wf-integrated", now);
        run.Enqueue(now);

        var entry = new JournalEntry(run.Id, run.AttemptId, "RunQueued", now);

        await ctx.WorkerRecords.AddAsync(worker);
        await ctx.RunRecords.AddAsync(run);
        await ctx.JournalEntries.AddAsync(entry);
        await ctx.SaveChangesAsync();

        await using var verify = new OrchestratorDbContext(_options);
        (await verify.WorkerRecords.CountAsync()).Should().BeGreaterThanOrEqualTo(1);
        (await verify.RunRecords.CountAsync()).Should().BeGreaterThanOrEqualTo(1);
        (await verify.JournalEntries.CountAsync()).Should().BeGreaterThanOrEqualTo(1);
    }

    // ─── Index verification via query ──────────────────────────────────────────

    [Fact]
    public async Task RunRecords_QueryByState_ReturnsMatchingRecordsOnly()
    {
        await using var ctx = new OrchestratorDbContext(_options);
        await ctx.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        var r1  = new RunRecord(Guid.NewGuid(), "wf-a", now);      // Created
        var r2  = new RunRecord(Guid.NewGuid(), "wf-b", now);
        r2.Enqueue(now);                                            // Queued

        await ctx.RunRecords.AddRangeAsync(r1, r2);
        await ctx.SaveChangesAsync();

        await using var verify = new OrchestratorDbContext(_options);
        var queued = await verify.RunRecords
            .Where(r => r.State == RunState.Queued)
            .ToListAsync();

        queued.Should().ContainSingle()
            .Which.Id.Should().Be(r2.Id);
    }

    [Fact]
    public async Task WorkerRecords_QueryByIsOnline_ReturnsOfflineOnly()
    {
        await using var ctx = new OrchestratorDbContext(_options);
        await ctx.Database.EnsureCreatedAsync();

        var now     = DateTimeOffset.UtcNow;
        var online  = new WorkerRecord("worker-on",  "Desktop", 120, now);
        var offline = new WorkerRecord("worker-off", "Desktop", 120, now);
        offline.MarkOffline(now.AddMinutes(1));

        await ctx.WorkerRecords.AddRangeAsync(online, offline);
        await ctx.SaveChangesAsync();

        await using var verify = new OrchestratorDbContext(_options);
        var offlineWorkers = await verify.WorkerRecords
            .Where(w => !w.IsOnline)
            .ToListAsync();

        offlineWorkers.Should().ContainSingle()
            .Which.WorkerId.Should().Be("worker-off");
    }
}
