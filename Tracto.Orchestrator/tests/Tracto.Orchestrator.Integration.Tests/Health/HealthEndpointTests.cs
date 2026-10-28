using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tracto.Orchestrator.Infrastructure.Persistence;

namespace Tracto.Orchestrator.Integration.Tests.Health;

/// <summary>
/// OC-16 — Integration tests for GET /health.
/// Uses WebApplicationFactory with an in-memory SQLite database so no real disk I/O occurs.
/// Verifies: 200 OK response, dbConnected=true, JSON shape, and mode reflection.
/// </summary>
public sealed class HealthEndpointTests : IClassFixture<HealthEndpointTests.ConnectedModeFactory>
{
    // ── WebApplicationFactory customised for in-memory SQLite ─────────────────

    /// <summary>
    /// Factory that starts the orchestrator in Connected mode with an in-memory SQLite database.
    /// Does NOT call AddDbContextFactory inside ConfigureServices — EF Core's internal
    /// IDbContextOptionsConfiguration registrations can be scoped, which causes
    /// "cannot resolve scoped service from root provider" when the Singleton factory
    /// is resolved at startup. Instead, DbContextOptions is built outside DI and the
    /// factory is registered directly as a Singleton.
    /// </summary>
    public sealed class ConnectedModeFactory : WebApplicationFactory<Program>
    {
        // Keep the SQLite in-memory connection alive for the lifetime of the factory.
        // Naming the DB and sharing the connection ensures all contexts see the same schema.
        private readonly SqliteConnection _keepAliveConnection;
        private readonly string? _previousMode;

        public ConnectedModeFactory()
        {
            _previousMode = Environment.GetEnvironmentVariable("TRACTO_MODE");
            // TRACTO_DB_PATH is only read by the design-time EF tooling factory; production
            // code uses the DI-registered IDbContextFactory which is replaced below.
            // Do NOT set TRACTO_DB_PATH here — it is dead mutation of process-wide state (M3 fix).

            // Pre-set TRACTO_MODE before the host builds so any early readers see it.
            Environment.SetEnvironmentVariable("TRACTO_MODE", "Connected");

            _keepAliveConnection = new SqliteConnection("DataSource=:memory:");
            _keepAliveConnection.Open();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Inject TRACTO_MODE through IConfiguration (UseSetting) so Program.cs reads it
            // from builder.Configuration — keeps the override scoped to this factory's host.
            builder.UseEnvironment("Testing");
            builder.UseSetting("TRACTO_MODE", "Connected");

            builder.ConfigureServices(services =>
            {
                // Remove ALL DI descriptors related to OrchestratorDbContext so no production
                // EF Core IDbContextOptionsConfiguration<T> registration remains.
                RemoveAllDbContextRelatedServices(services);

                // Build options using the keep-alive connection so all contexts share the same DB.
                var dbOpts = new DbContextOptionsBuilder<OrchestratorDbContext>()
                    .UseSqlite(_keepAliveConnection)
                    .Options;

                services.AddSingleton<IDbContextFactory<OrchestratorDbContext>>(
                    new OrchestratorDbContextFactory(dbOpts));

                // Re-register OrchestratorDbContext as transient via the factory so
                // repositories that inject it directly (EfRunRepository etc.) can resolve it.
                services.AddTransient<OrchestratorDbContext>(sp =>
                    sp.GetRequiredService<IDbContextFactory<OrchestratorDbContext>>().CreateDbContext());

                // Ensure the in-memory SQLite schema exists before any hosted service runs.
                // The keep-alive connection holds the DB alive after this context is disposed.
                using var ctx = new OrchestratorDbContext(dbOpts);
                ctx.Database.EnsureCreated();
            });
        }

        private static void RemoveAllDbContextRelatedServices(IServiceCollection services)
        {
            var ctx = typeof(OrchestratorDbContext);
            var toRemove = services
                .Where(d => IsRelatedToContext(d.ServiceType, ctx) ||
                            (d.ImplementationType is not null &&
                             IsRelatedToContext(d.ImplementationType, ctx)))
                .ToList();
            foreach (var d in toRemove)
                services.Remove(d);
        }

        private static bool IsRelatedToContext(Type t, Type ctx)
        {
            if (t == ctx) return true;
            return t.IsGenericType && t.GenericTypeArguments.Any(a => a == ctx);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _keepAliveConnection.Dispose();
                // Restore TRACTO_MODE to the value it held before this factory was created.
                // Passing null removes the variable when it was not set originally.
                Environment.SetEnvironmentVariable("TRACTO_MODE", _previousMode);
                // TRACTO_DB_PATH was not mutated by this factory (M3 fix), so no restore needed.
            }
            base.Dispose(disposing);
        }
    }

    private readonly ConnectedModeFactory _factory;

    public HealthEndpointTests(ConnectedModeFactory factory)
    {
        _factory = factory;
    }

    // ── Tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_Health_Returns200OK()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "the health endpoint must always return 200 OK when the host is reachable");
    }

    [Fact]
    public async Task Get_Health_BodyContainsHealthyStatus()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");
        var json     = await ParseJsonAsync(response);

        // Assert
        json.GetProperty("status").GetString()
            .Should().Be("healthy", because: "status field must always be 'healthy'");
    }

    [Fact]
    public async Task Get_Health_DbConnectedIsTrue_WithInMemorySqlite()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");
        var json     = await ParseJsonAsync(response);

        // Assert
        // OC-16 AC: health 200 OK; dbConnected=true confirms the EF Core context can reach the DB.
        json.GetProperty("dbConnected").GetBoolean()
            .Should().BeTrue(because: "in-memory SQLite must always be reachable");
    }

    [Fact]
    public async Task Get_Health_QueueDepthIsZero_InSprint1()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");
        var json     = await ParseJsonAsync(response);

        // Assert — queueDepth is 0 in Sprint 1; it will be populated when OC-01 is implemented.
        json.GetProperty("queueDepth").GetInt32()
            .Should().Be(0, because: "queue subsystem is not yet implemented in Sprint 1");
    }

    [Fact]
    public async Task Get_Health_ModeReflectsDeploymentMode()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");
        var json     = await ParseJsonAsync(response);

        // Assert — mode should be "Connected" as set in ConnectedModeFactory.
        json.GetProperty("mode").GetString()
            .Should().Be("Connected", because: "factory sets TRACTO_MODE=Connected");
    }

    [Fact]
    public async Task Get_Health_ContentTypeIsApplicationJson()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        response.Content.Headers.ContentType?.MediaType
            .Should().Be("application/json", because: "health endpoint must return JSON");
    }

    // ── Isolated mode factory + test ───────────────────────────────────────────

    [Fact]
    public async Task Get_Health_IsolatedMode_Returns200OK()
    {
        // Arrange — spin up a separate factory for Isolated mode.
        // NOTE: Isolated mode binds 127.0.0.1; WebApplicationFactory uses an in-process
        //       test server so the actual bind address does not matter for this test.
        //
        // MAJOR fix: inject TRACTO_MODE via UseSetting (IConfiguration) rather than relying
        // solely on Environment.SetEnvironmentVariable.  Program.cs reads from
        // builder.Configuration first, so UseSetting is both correct and race-safe (it does
        // not mutate the process-wide environment that other threads can observe).
        // We still set the env var as a belt-and-suspenders guard for any early reader, and
        // restore it properly in the finally block using the true original (null when not set
        // before this test), never defaulting to "Connected" which could mask a missing var.
        var previousMode = Environment.GetEnvironmentVariable("TRACTO_MODE");
        try
        {
            Environment.SetEnvironmentVariable("TRACTO_MODE", "Isolated");

            // Keep-alive connection so the in-memory DB persists for the lifetime of this factory.
            await using var isolatedKeepAlive = new SqliteConnection("DataSource=:memory:");
            await isolatedKeepAlive.OpenAsync();

            await using var isolatedFactory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(b =>
                {
                    b.UseEnvironment("Testing");
                    // Primary injection path: IConfiguration override scoped to this host.
                    b.UseSetting("TRACTO_MODE", "Isolated");
                    b.ConfigureServices(services =>
                    {
                        // Remove production EF Core registrations.
                        var ctx = typeof(OrchestratorDbContext);
                        var toRemove = services
                            .Where(d => IsGenericRelated(d.ServiceType, ctx) ||
                                        (d.ImplementationType is not null &&
                                         IsGenericRelated(d.ImplementationType, ctx)))
                            .ToList();
                        foreach (var d in toRemove) services.Remove(d);

                        // Register in-memory factory using the keep-alive connection.
                        var dbOpts = new DbContextOptionsBuilder<OrchestratorDbContext>()
                            .UseSqlite(isolatedKeepAlive)
                            .Options;
                        services.AddSingleton<IDbContextFactory<OrchestratorDbContext>>(
                            new OrchestratorDbContextFactory(dbOpts));
                        // Re-register OrchestratorDbContext as transient via the factory.
                        services.AddTransient<OrchestratorDbContext>(sp =>
                            sp.GetRequiredService<IDbContextFactory<OrchestratorDbContext>>().CreateDbContext());
                        // Ensure schema exists; keep-alive connection holds the DB alive after dispose.
                        using var ctx2 = new OrchestratorDbContext(dbOpts);
                        ctx2.Database.EnsureCreated();
                    });
                });

            var client = isolatedFactory.CreateClient();

            // Act
            var response = await client.GetAsync("/health");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK,
                because: "health endpoint must return 200 OK in Isolated mode");
        }
        finally
        {
            // Restore the env var to its true original value (null when it was not set),
            // not a hard-coded "Connected" fallback that could mask a genuinely missing var.
            Environment.SetEnvironmentVariable("TRACTO_MODE", previousMode);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static bool IsGenericRelated(Type t, Type ctx)
    {
        if (t == ctx) return true;
        return t.IsGenericType && t.GenericTypeArguments.Any(a => a == ctx);
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content).RootElement;
    }
}
