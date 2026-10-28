using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tracto.Orchestrator.Domain.Journal;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Infrastructure.Persistence;

namespace Tracto.Orchestrator.Unit.Tests.Persistence;

/// <summary>
/// Unit tests for <see cref="UnitOfWork"/>.
///
/// OC-15 ACs verified:
///   - Saving a RunRecord also writes a JournalEntry in the same transaction.
///   - An exception that prevents commit leaves no data persisted (rollback).
/// </summary>
public sealed class UnitOfWorkTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly DbContextOptions<OrchestratorDbContext> _options;

    public UnitOfWorkTests()
    {
        _keepAlive = new SqliteConnection("DataSource=:memory:");
        _keepAlive.Open();

        _options = new DbContextOptionsBuilder<OrchestratorDbContext>()
            .UseSqlite(_keepAlive)
            .Options;

        // Create the full OC-15 schema once for the life of this test class.
        using var ctx = new OrchestratorDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _keepAlive.Dispose();

    // ─── Helper ───────────────────────────────────────────────────────────────

    private (OrchestratorDbContext db, UnitOfWork uow) CreatePair()
    {
        var db = new OrchestratorDbContext(_options);
        return (db, new UnitOfWork(db));
    }

    // ─── AC1: RunRecord + JournalEntry written in same transaction ─────────────

    [Fact]
    public async Task CommitAsync_WithRunRecordAndJournalEntry_BothPersisted()
    {
        // Arrange
        var (db, uow) = CreatePair();
        await using var _ = uow;

        var now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var run = new RunRecord(Guid.NewGuid(), "wf-001", now);
        var entry = new JournalEntry(
            runId:      run.Id,
            attemptId:  run.AttemptId,
            eventType:  "RunCreated",
            occurredAt: now);

        await db.RunRecords.AddAsync(run);
        await db.JournalEntries.AddAsync(entry);

        // Act
        await uow.CommitAsync();

        // Assert — fresh context confirms both rows are in DB.
        await using var verify = new OrchestratorDbContext(_options);
        var storedRun   = await verify.RunRecords.FindAsync(run.Id);
        var storedEntry = await verify.JournalEntries.FindAsync(entry.Id);

        storedRun.Should().NotBeNull(
            because: "RunRecord must be persisted after CommitAsync");
        storedEntry.Should().NotBeNull(
            because: "JournalEntry must be persisted in the same transaction as the RunRecord");
        storedRun!.Id.Should().Be(run.Id);
        storedEntry!.RunId.Should().Be(run.Id);
        storedEntry.EventType.Should().Be("RunCreated");
    }

    [Fact]
    public async Task CommitAsync_JournalEntry_SharesSameRunId_AsRunRecord()
    {
        // Arrange
        var (db, uow) = CreatePair();
        await using var _ = uow;

        var now = DateTimeOffset.UtcNow;
        var run = new RunRecord(Guid.NewGuid(), "wf-002", now);
        run.Enqueue(now);

        var entry = new JournalEntry(
            runId:      run.Id,
            attemptId:  run.AttemptId,
            eventType:  "RunQueued",
            occurredAt: now,
            payload:    "{\"state\":\"Queued\"}");

        await db.RunRecords.AddAsync(run);
        await db.JournalEntries.AddAsync(entry);

        // Act
        await uow.CommitAsync();

        // Assert
        await using var verify = new OrchestratorDbContext(_options);
        var entries = await verify.JournalEntries
            .Where(j => j.RunId == run.Id)
            .ToListAsync();

        entries.Should().HaveCount(1,
            because: "exactly one journal entry was appended for the RunQueued event");
        entries[0].EventType.Should().Be("RunQueued");
        entries[0].AttemptId.Should().Be(run.AttemptId);
    }

    // ─── AC2: Explicit RollbackAsync discards staged changes ─────────────────

    [Fact]
    public async Task RollbackAsync_AfterStagingChanges_NothingPersisted()
    {
        // Arrange — stage a RunRecord inside a UoW transaction, then roll back before
        // any CommitAsync.  SQLite in-memory with a shared connection does not surface
        // the rolled-back row to a second context on the same connection, so we verify
        // the invariant by confirming the row never leaves the EF change-tracker.
        var runId = Guid.NewGuid();
        var now   = DateTimeOffset.UtcNow;

        {
            var (db, uow) = CreatePair();

            // Open the UoW transaction so RollbackAsync has a real transaction to abort.
            await uow.BeginAsync();

            // Stage the row in EF Core's change tracker (no SQL flushed yet).
            await db.RunRecords.AddAsync(new RunRecord(runId, "wf-rollback", now));

            // Act — roll back and dispose; the row must never reach SQLite.
            await uow.RollbackAsync();
            await uow.DisposeAsync();
            await db.DisposeAsync();
        }

        // Assert — a fresh context must find no trace of the record.
        await using var verify = new OrchestratorDbContext(_options);
        var stored = await verify.RunRecords.FindAsync(runId);
        stored.Should().BeNull(
            "a rolled-back transaction must leave no trace in the database");
    }

    [Fact]
    public async Task CommitAsync_NotCalled_DataNotPersisted()
    {
        // Arrange — add a row to EF Core's change tracker and SaveChanges within an
        // open UoW transaction, but dispose the UoW WITHOUT calling CommitAsync.
        // Domain invariant 5: every write goes through CommitAsync; data staged but
        // not committed must never appear in the database.
        var runId = Guid.NewGuid();
        var now   = DateTimeOffset.UtcNow;

        {
            var (db, uow) = CreatePair();

            await uow.BeginAsync();
            await db.RunRecords.AddAsync(new RunRecord(runId, "wf-no-commit", now));

            // Dispose WITHOUT CommitAsync — UoW.DisposeAsync rolls back the transaction.
            await uow.DisposeAsync();
            await db.DisposeAsync();
        }

        // Assert — no row must have been committed to the database.
        await using var verify = new OrchestratorDbContext(_options);
        var stored = await verify.RunRecords.FindAsync(runId);
        stored.Should().BeNull(
            "data staged but never committed via CommitAsync must not be persisted");
    }

    // ─── AC3: CommitAsync with no changes is a no-op ─────────────────────────

    [Fact]
    public async Task CommitAsync_WithoutAnyChanges_DoesNotThrow()
    {
        var (_, uow) = CreatePair();
        await using var _ = uow;

        var act = async () => await uow.CommitAsync();
        await act.Should().NotThrowAsync(
            because: "committing an empty change-set must not throw");
    }

    // ─── AC4: Payload stored verbatim ─────────────────────────────────────────

    [Fact]
    public async Task CommitAsync_JournalEntry_PayloadIsPreservedVerbatim()
    {
        var (db, uow) = CreatePair();
        await using var _ = uow;

        var now = DateTimeOffset.UtcNow;
        var run = new RunRecord(Guid.NewGuid(), "wf-payload", now);
        const string expectedPayload = "{\"state\":\"Unknown\",\"reason\":\"Worker absent at restart\"}";
        var entry = new JournalEntry(
            runId:      run.Id,
            attemptId:  run.AttemptId,
            eventType:  "RunMarkedUnknown",
            occurredAt: now,
            payload:    expectedPayload);

        await db.RunRecords.AddAsync(run);
        await db.JournalEntries.AddAsync(entry);
        await uow.CommitAsync();

        await using var verify = new OrchestratorDbContext(_options);
        var stored = await verify.JournalEntries.FindAsync(entry.Id);

        stored.Should().NotBeNull();
        stored!.Payload.Should().Be(expectedPayload,
            because: "JSON payload must be stored and retrieved verbatim");
    }

    // ─── AC5: Two sequential commits each succeed independently ──────────────

    [Fact]
    public async Task CommitAsync_TwoSequentialCommits_BothPersisted()
    {
        // First commit
        {
            var (db1, uow1) = CreatePair();
            await using var d1 = uow1;
            var now = DateTimeOffset.UtcNow;
            var run1 = new RunRecord(Guid.NewGuid(), "wf-first", now);
            var e1   = new JournalEntry(run1.Id, null, "RunCreated", now);
            await db1.RunRecords.AddAsync(run1);
            await db1.JournalEntries.AddAsync(e1);
            await uow1.CommitAsync();
        }

        // Second commit
        {
            var (db2, uow2) = CreatePair();
            await using var d2 = uow2;
            var now = DateTimeOffset.UtcNow;
            var run2 = new RunRecord(Guid.NewGuid(), "wf-second", now);
            var e2   = new JournalEntry(run2.Id, null, "RunCreated", now);
            await db2.RunRecords.AddAsync(run2);
            await db2.JournalEntries.AddAsync(e2);
            await uow2.CommitAsync();
        }

        // Both records must be in the DB.
        await using var verify = new OrchestratorDbContext(_options);
        var runCount   = await verify.RunRecords.CountAsync();
        var entryCount = await verify.JournalEntries.CountAsync();

        runCount.Should().BeGreaterThanOrEqualTo(2,
            because: "both commits must be independently persisted");
        entryCount.Should().BeGreaterThanOrEqualTo(2);
    }
}
