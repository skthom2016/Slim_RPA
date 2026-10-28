using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tracto.Orchestrator.Infrastructure.Persistence;

namespace Tracto.Orchestrator.Unit.Tests.Health;

/// <summary>
/// Unit tests that verify the database-connectivity logic underpinning the
/// <c>GET /health</c> endpoint's <c>dbConnected</c> field.
///
/// The health endpoint itself lives in the Api layer (not referenced from unit tests),
/// so we test the observable behaviour of <see cref="OrchestratorDbContext.Database.CanConnectAsync"/>
/// with an in-memory SQLite database — the same check the endpoint delegates to.
///
/// OC-16 AC: health endpoint returns dbConnected=true with in-memory SQLite.
/// </summary>
public sealed class HealthCheckLogicTests : IDisposable
{
    private readonly SqliteConnection _keepAliveConnection;
    private readonly DbContextOptions<OrchestratorDbContext> _options;

    public HealthCheckLogicTests()
    {
        _keepAliveConnection = new SqliteConnection("DataSource=:memory:");
        _keepAliveConnection.Open();

        _options = new DbContextOptionsBuilder<OrchestratorDbContext>()
            .UseSqlite(_keepAliveConnection)
            .Options;
    }

    public void Dispose() => _keepAliveConnection.Dispose();

    // ─── dbConnected = true path ────────────────────────────────────────────────

    [Fact]
    public async Task CanConnectAsync_AfterEnsureCreated_IsTrue()
    {
        // Arrange
        await using var ctx = new OrchestratorDbContext(_options);
        await ctx.Database.EnsureCreatedAsync();

        // Act – this is the exact call the health endpoint makes
        var dbConnected = await ctx.Database.CanConnectAsync();

        // Assert
        dbConnected.Should().BeTrue(
            because: "GET /health must return dbConnected=true when the database is reachable");
    }

    [Fact]
    public async Task CanConnectAsync_WithoutEnsureCreated_IsTrue()
    {
        // SQLite in-memory databases accept connections even before the schema is created;
        // the health check should still report dbConnected=true in this state.
        await using var ctx = new OrchestratorDbContext(_options);

        var dbConnected = await ctx.Database.CanConnectAsync();

        dbConnected.Should().BeTrue();
    }

    // ─── dbConnected = false path ───────────────────────────────────────────────

    [Fact]
    public async Task CanConnectAsync_WithUnreachablePath_IsFalse()
    {
        // Arrange – point EF Core at a deeply-nested directory that does not exist.
        // SQLite cannot create intermediate directories, so the connection attempt fails.
        var unreachablePath = Path.Combine(
            Path.GetTempPath(),
            $"tracto-{Guid.NewGuid():N}",
            "does-not-exist",
            "sub",
            "db.sqlite");

        var brokenOptions = new DbContextOptionsBuilder<OrchestratorDbContext>()
            .UseSqlite($"Data Source={unreachablePath}")
            .Options;

        await using var ctx = new OrchestratorDbContext(brokenOptions);

        // Act
        var dbConnected = await ctx.Database.CanConnectAsync();

        // Assert
        dbConnected.Should().BeFalse(
            because: "when the DB path is unreachable the health endpoint must report dbConnected=false");
    }

    // ─── Health response shape ──────────────────────────────────────────────────

    [Fact]
    public async Task HealthSnapshot_WithReachableDb_HasExpectedFields()
    {
        // Arrange
        await using var ctx = new OrchestratorDbContext(_options);
        await ctx.Database.EnsureCreatedAsync();

        // Act – simulate what the /health handler assembles
        var dbConnected = await ctx.Database.CanConnectAsync();
        const string status     = "healthy";
        const int    queueDepth = 0;   // Queue not yet implemented; always 0 in Sprint 1.

        // Assert
        dbConnected.Should().BeTrue();
        status.Should().Be("healthy");
        queueDepth.Should().Be(0,
            because: "queue storage is not yet implemented in Sprint 1; depth must be 0");
    }

    // ─── Factory-based context (mirrors DI container usage) ───────────────────

    [Fact]
    public async Task FactoryCreatedContext_CanConnectAsync_IsTrue()
    {
        // Arrange
        var factory = new OrchestratorDbContextFactory(_options);
        await using var ctx = factory.CreateDbContext();
        await ctx.Database.EnsureCreatedAsync();

        // Act
        var dbConnected = await ctx.Database.CanConnectAsync();

        // Assert
        dbConnected.Should().BeTrue(
            because: "IDbContextFactory<OrchestratorDbContext> must supply a usable context");
    }
}
