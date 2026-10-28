using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tracto.Orchestrator.Infrastructure.Persistence;

namespace Tracto.Orchestrator.Unit.Tests.Persistence;

/// <summary>
/// Unit tests for <see cref="OrchestratorDbContext"/>.
/// Uses an in-memory SQLite connection so no real file I/O occurs.
/// No Thread.Sleep, no external process, no EF migrations required.
///
/// OC-16 AC: health endpoint returns dbConnected=true with in-memory SQLite.
/// </summary>
public sealed class OrchestratorDbContextTests : IDisposable
{
    // A shared in-memory SQLite connection that stays open for the lifetime of the test
    // so the in-memory database is not discarded between context instances.
    private readonly SqliteConnection _keepAliveConnection;
    private readonly DbContextOptions<OrchestratorDbContext> _options;

    public OrchestratorDbContextTests()
    {
        // SQLite in-memory databases are destroyed when their last connection closes,
        // so we keep one connection open for the duration of each test class instance.
        _keepAliveConnection = new SqliteConnection("DataSource=:memory:");
        _keepAliveConnection.Open();

        _options = new DbContextOptionsBuilder<OrchestratorDbContext>()
            .UseSqlite(_keepAliveConnection)
            .Options;
    }

    public void Dispose()
    {
        _keepAliveConnection.Dispose();
    }

    // ─── Schema creation ────────────────────────────────────────────────────────

    [Fact]
    public void EnsureCreated_WithInMemorySqlite_DoesNotThrow()
    {
        // Arrange
        using var ctx = new OrchestratorDbContext(_options);

        // Act
        var act = () => ctx.Database.EnsureCreated();

        // Assert
        act.Should().NotThrow(
            because: "the empty schema must be creatable without any pending migrations");
    }

    [Fact]
    public async Task EnsureCreatedAsync_WithInMemorySqlite_DoesNotThrow()
    {
        // Arrange
        await using var ctx = new OrchestratorDbContext(_options);

        // Act
        var act = async () => await ctx.Database.EnsureCreatedAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }

    // ─── Connectivity ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CanConnectAsync_WithInMemorySqlite_ReturnsTrue()
    {
        // Arrange
        await using var ctx = new OrchestratorDbContext(_options);
        await ctx.Database.EnsureCreatedAsync();

        // Act
        var canConnect = await ctx.Database.CanConnectAsync();

        // Assert
        canConnect.Should().BeTrue(
            because: "the health endpoint must report dbConnected=true when the DB is reachable");
    }

    [Fact]
    public void CanConnect_WithInMemorySqlite_ReturnsTrue()
    {
        // Arrange
        using var ctx = new OrchestratorDbContext(_options);
        ctx.Database.EnsureCreated();

        // Act
        var canConnect = ctx.Database.CanConnect();

        // Assert
        canConnect.Should().BeTrue();
    }

    // ─── Constructor and options acceptance ────────────────────────────────────

    [Fact]
    public void Constructor_AcceptsOptions_DoesNotThrow()
    {
        // Act & Assert
        var act = () =>
        {
            using var ctx = new OrchestratorDbContext(_options);
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_WithSqliteFile_AcceptsOptions()
    {
        // Arrange – file-based options (file is never actually created in this unit test)
        var fileOptions = new DbContextOptionsBuilder<OrchestratorDbContext>()
            .UseSqlite("Data Source=unit-test-placeholder.db")
            .Options;

        // Act & Assert – construction itself must not throw
        var act = () =>
        {
            using var ctx = new OrchestratorDbContext(fileOptions);
        };

        act.Should().NotThrow(
            because: "option-binding must succeed even before the file is opened");
    }

    // ─── Factory ────────────────────────────────────────────────────────────────

    [Fact]
    public void OrchestratorDbContextFactory_CreateDbContext_ReturnsFreshContext()
    {
        // Arrange
        var factory = new OrchestratorDbContextFactory(_options);

        // Act
        using var ctx = factory.CreateDbContext();

        // Assert
        ctx.Should().NotBeNull();
        ctx.Should().BeOfType<OrchestratorDbContext>();
    }

    [Fact]
    public void OrchestratorDbContextFactory_TwoCallsReturnDistinctInstances()
    {
        // Arrange
        var factory = new OrchestratorDbContextFactory(_options);

        // Act
        using var ctx1 = factory.CreateDbContext();
        using var ctx2 = factory.CreateDbContext();

        // Assert – each call must return a new, independent context
        ctx1.Should().NotBeSameAs(ctx2,
            because: "IDbContextFactory must return a new instance on each call");
    }

    // ─── Idempotency of EnsureCreated ──────────────────────────────────────────

    [Fact]
    public async Task EnsureCreatedAsync_CalledTwice_DoesNotThrow()
    {
        // Both calls against the same in-memory database must be safe (idempotent).
        await using var ctx1 = new OrchestratorDbContext(_options);
        await ctx1.Database.EnsureCreatedAsync();

        await using var ctx2 = new OrchestratorDbContext(_options);
        var act = async () => await ctx2.Database.EnsureCreatedAsync();

        await act.Should().NotThrowAsync(
            because: "EnsureCreated must be idempotent — the schema already exists on the second call");
    }
}
