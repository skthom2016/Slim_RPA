using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Tracto.Orchestrator.Infrastructure.Persistence;

/// <summary>
/// Runtime factory registered with DI as <see cref="IDbContextFactory{OrchestratorDbContext}"/>.
/// Short-lived contexts created here are used by command handlers and the health endpoint.
/// </summary>
public sealed class OrchestratorDbContextFactory : IDbContextFactory<OrchestratorDbContext>
{
    private readonly DbContextOptions<OrchestratorDbContext> _options;

    public OrchestratorDbContextFactory(DbContextOptions<OrchestratorDbContext> options)
        => _options = options;

    public OrchestratorDbContext CreateDbContext() => new(_options);
}

/// <summary>
/// Design-time factory used by EF Core tooling (dotnet ef migrations add, etc.).
/// Reads <c>TRACTO_DB_PATH</c> from the environment; falls back to a local dev file.
/// This class is never called at runtime.
/// </summary>
public sealed class OrchestratorDbContextDesignTimeFactory : IDesignTimeDbContextFactory<OrchestratorDbContext>
{
    public OrchestratorDbContext CreateDbContext(string[] args)
    {
        var dbPath = Environment.GetEnvironmentVariable("TRACTO_DB_PATH")
                     ?? "orchestrator-design.db";

        var optionsBuilder = new DbContextOptionsBuilder<OrchestratorDbContext>();
        optionsBuilder.UseSqlite($"Data Source={dbPath}");

        return new OrchestratorDbContext(optionsBuilder.Options);
    }
}
