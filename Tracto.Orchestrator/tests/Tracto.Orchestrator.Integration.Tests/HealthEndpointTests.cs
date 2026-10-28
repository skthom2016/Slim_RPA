using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tracto.Orchestrator.Infrastructure.Persistence;

namespace Tracto.Orchestrator.Integration.Tests;

/// <summary>
/// Integration tests for GET /health.
/// Uses WebApplicationFactory with an in-memory SQLite database.
/// No Thread.Sleep — synchronous connectivity probe completes immediately.
/// </summary>
public sealed class HealthEndpointTests : IClassFixture<HealthEndpointTests.OrchestratorFactory>, IDisposable
{
    private readonly OrchestratorFactory _factory;
    private readonly HttpClient _client;

    public HealthEndpointTests(OrchestratorFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task GetHealth_Returns200Ok()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetHealth_ReturnsStatusHealthy()
    {
        var response = await _client.GetAsync("/health");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("status").GetString().Should().Be("healthy");
    }

    [Fact]
    public async Task GetHealth_DbConnected_IsTrue_WithInMemorySqlite()
    {
        var response = await _client.GetAsync("/health");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // The factory swaps the registered DbContextFactory for in-memory SQLite.
        // dbConnected must be true because EnsureCreatedAsync succeeds.
        body.GetProperty("dbConnected").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetHealth_QueueDepth_IsZero_OnFreshDatabase()
    {
        var response = await _client.GetAsync("/health");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Fresh in-memory SQLite: no runs in the database.
        body.GetProperty("queueDepth").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task GetHealth_Mode_ReflectsConfiguredDeploymentMode()
    {
        // The factory sets TRACTO_MODE=Connected.
        var response = await _client.GetAsync("/health");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("mode").GetString().Should().Be("Connected");
    }

    public void Dispose() => _client.Dispose();

    // ── Custom WebApplicationFactory ────────────────────────────────────────────
    /// <summary>
    /// Custom <see cref="WebApplicationFactory{TEntryPoint}"/> that:
    /// <list type="bullet">
    ///   <item>Sets TRACTO_MODE=Connected so Program.cs startup gate passes.</item>
    ///   <item>Replaces the production EF Core registration with an in-memory SQLite factory
    ///         that shares a single keep-alive connection (so EnsureCreated and all queries
    ///         see the same schema).</item>
    /// </list>
    /// The factory does NOT call <c>AddDbContextFactory</c> in ConfigureServices — doing so
    /// would add EF Core's internal <c>IDbContextOptionsConfiguration&lt;T&gt;</c> services
    /// which may be Scoped, causing "cannot resolve scoped service from root provider" when
    /// the Singleton <c>IDbContextFactory&lt;T&gt;</c> is resolved during startup.
    /// Instead, <c>DbContextOptions</c> are built outside DI and the factory is registered
    /// directly as a plain Singleton.
    /// </summary>
    public sealed class OrchestratorFactory : WebApplicationFactory<Program>, IDisposable
    {
        // Use a named shared-cache in-memory SQLite database.
        // Unlike anonymous ":memory:" databases (where each new SqliteConnection opens a
        // fresh empty DB), a named shared-cache database is shared across ALL connections
        // that use the same name — so EF Core migrations, repositories, and the health
        // endpoint all see the same schema even when they open their own connections.
        private static readonly string DbName =
            $"tracto-test-{Guid.NewGuid():N}";

        private readonly string _connectionString =
            $"Data Source={DbName};Mode=Memory;Cache=Shared";

        private readonly string? _previousMode;
        // Keep-alive connection prevents the named shared-cache database from being
        // destroyed when Program.cs disposes its EnsureCreated context before
        // RestartRecoveryService.StartAsync runs.
        private readonly Microsoft.Data.Sqlite.SqliteConnection _keepAlive;

        public OrchestratorFactory()
        {
            // Save the original value so Dispose() can restore it, avoiding permanent
            // process-wide env var mutation that would pollute other tests.
            _previousMode = Environment.GetEnvironmentVariable("TRACTO_MODE");

            // Set the required env var BEFORE the factory builds the host.
            // ConfigureWebHost will also inject this via UseSetting for IConfiguration,
            // but some code paths may still read the raw env var.
            Environment.SetEnvironmentVariable("TRACTO_MODE", "Connected");

            // Open the keep-alive BEFORE the host builds so the named shared-cache
            // database exists when Program.cs calls EnsureCreatedAsync.
            _keepAlive = new Microsoft.Data.Sqlite.SqliteConnection(_connectionString);
            _keepAlive.Open();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Inject TRACTO_MODE through IConfiguration (UseSetting) so that
            // Program.cs reads it from builder.Configuration rather than the process env,
            // keeping the override scoped to this factory's host and avoiding a race if
            // the env var is mutated by another concurrent test setup.
            builder.UseSetting("TRACTO_MODE", "Connected");
            // UseEnvironment("Testing") tells Program.cs to use EnsureCreatedAsync
            // instead of MigrateAsync, which is required for in-memory SQLite.
            builder.UseEnvironment("Testing");

            builder.ConfigureServices(services =>
            {
                // Remove ALL DI descriptors that reference OrchestratorDbContext by service type
                // or generic type argument — IDbContextFactory<T>, DbContextOptions<T>,
                // IDbContextOptionsConfiguration<T>, scoped DbContext, etc.
                RemoveAllDbContextRelatedServices(services);

                // Build options from the named shared-cache connection string so that every
                // new SqliteConnection opened by EF Core (migrations, repos, health endpoint)
                // resolves to the same named in-memory database.
                var dbOpts = new DbContextOptionsBuilder<OrchestratorDbContext>()
                    .UseSqlite(_connectionString)
                    .Options;

                // Register as a plain Singleton — no EF Core DI lifetime conflicts.
                services.AddSingleton<IDbContextFactory<OrchestratorDbContext>>(
                    new OrchestratorDbContextFactory(dbOpts));

                // Re-register OrchestratorDbContext as transient via the factory so
                // repositories that inject it directly (EfRunRepository etc.) can resolve it.
                services.AddTransient<OrchestratorDbContext>(sp =>
                    sp.GetRequiredService<IDbContextFactory<OrchestratorDbContext>>().CreateDbContext());

                // Schema is created by Program.cs EnsureCreatedAsync (Testing env).
                // Do NOT call EnsureCreated here — the host isn't fully built yet and
                // the schema will be applied before any hosted service starts.
            });
        }

        /// <summary>
        /// Removes every descriptor whose <c>ServiceType</c> or <c>ImplementationType</c>
        /// is generically related to <see cref="OrchestratorDbContext"/>.
        /// </summary>
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
                // Restore TRACTO_MODE to its original value so the process env var is not
                // permanently mutated after this factory is torn down.  Passing null removes
                // the variable entirely, which matches the pre-test state when it was not set.
                Environment.SetEnvironmentVariable("TRACTO_MODE", _previousMode);
                _keepAlive.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
