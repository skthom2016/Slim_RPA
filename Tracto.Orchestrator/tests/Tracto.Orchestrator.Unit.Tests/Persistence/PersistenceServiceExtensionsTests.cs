using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tracto.Orchestrator.Domain.Settings;
using Tracto.Orchestrator.Infrastructure.Persistence;

namespace Tracto.Orchestrator.Unit.Tests.Persistence;

/// <summary>
/// Unit tests for <see cref="PersistenceServiceExtensions"/>.
/// Verifies that the DI helper methods register <see cref="IDbContextFactory{OrchestratorDbContext}"/>
/// correctly so that the runtime application context can resolve and use them without exception.
///
/// The production registration uses only IDbContextFactory (no scoped AddDbContext) to avoid
/// "Cannot resolve scoped service from root provider" errors in singleton factory scenarios.
///
/// Uses in-memory SQLite so no real file I/O occurs and the tests remain fast and portable.
/// No Thread.Sleep is used.
/// </summary>
public sealed class PersistenceServiceExtensionsTests
{
    // ─── AddOrchestratorPersistenceInMemory ────────────────────────────────────

    [Fact]
    public void AddOrchestratorPersistenceInMemory_RegistersDbContext()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddOrchestratorPersistenceInMemory("oc16-test-db");

        // Act
        var provider = services.BuildServiceProvider();
        var factory  = provider.GetService<IDbContextFactory<OrchestratorDbContext>>();

        // Assert — OrchestratorDbContext is reachable via the registered factory.
        factory.Should().NotBeNull(
            because: "OrchestratorDbContext must be reachable via IDbContextFactory registered by AddOrchestratorPersistenceInMemory");

        using var ctx = factory!.CreateDbContext();
        ctx.Should().NotBeNull(because: "factory must produce a non-null OrchestratorDbContext");
    }

    [Fact]
    public void AddOrchestratorPersistenceInMemory_RegistersDbContextFactory()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddOrchestratorPersistenceInMemory("oc16-factory-test");

        // Act
        var provider = services.BuildServiceProvider();
        var factory  = provider.GetService<IDbContextFactory<OrchestratorDbContext>>();

        // Assert
        factory.Should().NotBeNull(
            because: "IDbContextFactory<OrchestratorDbContext> must be registered for use in command handlers");
    }

    [Fact]
    public void AddOrchestratorPersistenceInMemory_FactoryCreatesUsableContext()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddOrchestratorPersistenceInMemory("oc16-usable-test");
        var provider = services.BuildServiceProvider();
        var factory  = provider.GetRequiredService<IDbContextFactory<OrchestratorDbContext>>();

        // Act
        using var ctx = factory.CreateDbContext();
        var act = () => ctx.Database.EnsureCreated();

        // Assert
        act.Should().NotThrow(
            because: "a context from the factory must be able to initialise the schema");
    }

    [Fact]
    public void AddOrchestratorPersistenceInMemory_DefaultDbName_DoesNotThrow()
    {
        // Omitting the database name should use the built-in default without throwing.
        var services = new ServiceCollection();
        var act = () => services.AddOrchestratorPersistenceInMemory();

        act.Should().NotThrow();
    }

    // ─── AddOrchestratorPersistence (file-based SQLite) ───────────────────────

    [Fact]
    public void AddOrchestratorPersistence_RegistersDbContext()
    {
        // Arrange – pass a temp path; the file is never actually opened in this test.
        var settings = new OrchestratorSettings(
            Mode:    DeploymentMode.Connected,
            DbPath:  Path.Combine(Path.GetTempPath(), $"oc16-unit-{Guid.NewGuid():N}.db"));

        var services = new ServiceCollection();
        services.AddOrchestratorPersistence(settings);

        // Act — OrchestratorDbContext is now accessed via IDbContextFactory (no scoped registration).
        var provider = services.BuildServiceProvider();
        var factory  = provider.GetService<IDbContextFactory<OrchestratorDbContext>>();

        // Assert
        factory.Should().NotBeNull(
            because: "AddOrchestratorPersistence must register IDbContextFactory<OrchestratorDbContext>");

        using var ctx = factory!.CreateDbContext();
        ctx.Should().NotBeNull(
            because: "the factory must produce a non-null OrchestratorDbContext");
    }

    [Fact]
    public void AddOrchestratorPersistence_RegistersDbContextFactory()
    {
        // Arrange
        var settings = new OrchestratorSettings(
            Mode:   DeploymentMode.Isolated,
            DbPath: Path.Combine(Path.GetTempPath(), $"oc16-factory-{Guid.NewGuid():N}.db"));

        var services = new ServiceCollection();
        services.AddOrchestratorPersistence(settings);

        // Act
        var provider = services.BuildServiceProvider();
        var factory  = provider.GetService<IDbContextFactory<OrchestratorDbContext>>();

        // Assert
        factory.Should().NotBeNull();
    }

    // ─── DbPath is reflected in the connection string ──────────────────────────

    [Fact]
    public void AddOrchestratorPersistence_ConnectionString_ContainsDbPath()
    {
        // Arrange
        const string expectedPath = "specific-test-path.db";
        var settings = new OrchestratorSettings(DeploymentMode.Connected, DbPath: expectedPath);

        var services = new ServiceCollection();
        services.AddOrchestratorPersistence(settings);
        var provider = services.BuildServiceProvider();

        // Act — obtain a context via the factory and inspect its connection string.
        var factory = provider.GetRequiredService<IDbContextFactory<OrchestratorDbContext>>();
        using var ctx = factory.CreateDbContext();
        var connectionString = ctx.Database.GetConnectionString();

        // Assert
        connectionString.Should().Contain(expectedPath,
            because: "the DbPath from OrchestratorSettings must be embedded in the connection string");
    }
}
