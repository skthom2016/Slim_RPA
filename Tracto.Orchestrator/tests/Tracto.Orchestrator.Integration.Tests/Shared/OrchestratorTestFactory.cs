#nullable enable
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Tracto.Orchestrator.Integration.Tests.Fixtures;
using Tracto.Orchestrator.Infrastructure.Persistence;

namespace Tracto.Orchestrator.Integration.Tests.Shared;

/// <summary>
/// Reusable WebApplicationFactory for all ORCH-IT integration tests.
///
/// Each test class that needs its own isolated SQLite database should instantiate
/// its own <see cref="OrchestratorTestFactory"/> (or a subclass) as an
/// <c>IClassFixture&lt;T&gt;</c>.
///
/// The factory:
///   - Sets TRACTO_MODE=Connected so Program.cs startup gate passes.
///   - Replaces production EF Core registrations with a named shared-cache
///     in-memory SQLite database.
///   - Opens a keep-alive connection so the schema survives between requests.
///   - Restores the original TRACTO_MODE on disposal.
/// </summary>
public class OrchestratorTestFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly string? _previousMode;
    private readonly SqliteConnection _keepAlive;
    private readonly string _tractoMode;

    public OrchestratorTestFactory(string tractoMode = "Connected")
    {
        _tractoMode = tractoMode;
        _previousMode = Environment.GetEnvironmentVariable("TRACTO_MODE");
        Environment.SetEnvironmentVariable("TRACTO_MODE", tractoMode);

        // Use a unique named shared-cache DB per factory instance so tests are isolated.
        var dbName = $"tracto-it-{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("TRACTO_MODE", _tractoMode);

        builder.ConfigureServices(services =>
        {
            RemoveAllDbContextRelatedServices(services);

            var dbOpts = new DbContextOptionsBuilder<OrchestratorDbContext>()
                .UseSqlite(_connectionString)
                // EF Core 9 + SQLite no longer supports ORDER BY DateTimeOffset; replace
                // the model customizer with one that converts all DateTimeOffset properties
                // to UTC ticks (long) so SQLite can sort them correctly.
                .ReplaceService<IModelCustomizer, DateTimeOffsetModelCustomizer>()
                .Options;

            services.AddSingleton<IDbContextFactory<OrchestratorDbContext>>(
                new OrchestratorDbContextFactory(dbOpts));

            // Scoped so repos and UoW share the same instance within a request.
            services.AddScoped<OrchestratorDbContext>(sp =>
                sp.GetRequiredService<IDbContextFactory<OrchestratorDbContext>>().CreateDbContext());

            // Eagerly create schema so it is ready before the first request.
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

    // A structurally-valid JWT accepted by JwtAuthMiddleware when no signing key is configured
    // (alg:none, parse-only mode).
    // Header: {"alg":"none","typ":"JWT"}  Payload: {"sub":"test-worker"}
    public const string FakeJwt =
        "eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0.eyJzdWIiOiJ0ZXN0LXdvcmtlciJ9.";

    /// <summary>
    /// Creates an <see cref="HttpClient"/> pre-configured with the fake Bearer token
    /// required by <c>JwtAuthMiddleware</c> for all /workers/* and /jobs/* routes.
    /// </summary>
    public HttpClient AuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "Authorization", $"Bearer {FakeJwt}");
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _keepAlive.Dispose();
            Environment.SetEnvironmentVariable("TRACTO_MODE", _previousMode);
        }
        base.Dispose(disposing);
    }
}
