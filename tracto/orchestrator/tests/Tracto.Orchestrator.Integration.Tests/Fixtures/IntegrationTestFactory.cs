#nullable enable
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Tracto.Orchestrator.Infrastructure.Persistence;

namespace Tracto.Orchestrator.Integration.Tests.Fixtures;

/// <summary>
/// Shared WebApplicationFactory base for all ORCH-IT integration tests.
/// Each test class can instantiate its own factory (isolated DB per class).
/// </summary>
public sealed class IntegrationTestFactory : WebApplicationFactory<Program>, IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly string? _previousMode;

    public IntegrationTestFactory()
    {
        _previousMode = Environment.GetEnvironmentVariable("TRACTO_MODE");
        Environment.SetEnvironmentVariable("TRACTO_MODE", "Connected");

        // Named shared-cache SQLite so all connections share one schema.
        var dbName = $"tracto-it-{Guid.NewGuid():N}";
        ConnectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _keepAlive = new SqliteConnection(ConnectionString);
        _keepAlive.Open();
    }

    public string ConnectionString { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("TRACTO_MODE", "Connected");

        builder.ConfigureServices(services =>
        {
            RemoveDbContextDescriptors(services);

            var dbOpts = new DbContextOptionsBuilder<OrchestratorDbContext>()
                .UseSqlite(ConnectionString)
                // EF Core 9 + SQLite no longer supports ORDER BY DateTimeOffset; replace the
                // model customizer with one that converts all DateTimeOffset properties to
                // UTC ticks (long) so SQLite can sort them correctly.
                .ReplaceService<IModelCustomizer, DateTimeOffsetModelCustomizer>()
                .Options;

            services.AddSingleton<IDbContextFactory<OrchestratorDbContext>>(
                new OrchestratorDbContextFactory(dbOpts));

            services.AddScoped<OrchestratorDbContext>(sp =>
                sp.GetRequiredService<IDbContextFactory<OrchestratorDbContext>>()
                  .CreateDbContext());
        });
    }

    private static void RemoveDbContextDescriptors(IServiceCollection services)
    {
        var ctxType = typeof(OrchestratorDbContext);
        var toRemove = services
            .Where(d => IsRelated(d.ServiceType, ctxType) ||
                        (d.ImplementationType is not null && IsRelated(d.ImplementationType, ctxType)))
            .ToList();
        foreach (var d in toRemove) services.Remove(d);
    }

    private static bool IsRelated(Type t, Type ctx)
    {
        if (t == ctx) return true;
        return t.IsGenericType && t.GenericTypeArguments.Any(a => a == ctx);
    }

    // A structurally-valid JWT accepted by JwtAuthMiddleware when no signing key is configured.
    // Header: {"alg":"none","typ":"JWT"}  Payload: {"sub":"test-worker"}
    public const string FakeJwt =
        "eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0.eyJzdWIiOiJ0ZXN0LXdvcmtlciJ9.";

    public HttpClient AuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {FakeJwt}");
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Environment.SetEnvironmentVariable("TRACTO_MODE", _previousMode);
            _keepAlive.Dispose();
        }
        base.Dispose(disposing);
    }
}
