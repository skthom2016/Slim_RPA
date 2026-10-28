using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Callbacks;
using Tracto.Orchestrator.Domain.Security;
using Tracto.Orchestrator.Domain.Settings;
using Tracto.Orchestrator.Infrastructure.Security;
using Tracto.Orchestrator.Infrastructure.Telemetry;
using AppReconciliationService = Tracto.Orchestrator.Application.Jobs.ReconciliationService;
using Tracto.Orchestrator.Infrastructure.Persistence.Artifacts;
using Tracto.Orchestrator.Infrastructure.Recovery;
using AppLeaseExpiryMonitorService = Tracto.Orchestrator.Application.Jobs.LeaseExpiryMonitorService;
using InfraLeaseExpiryMonitorService = Tracto.Orchestrator.Infrastructure.Recovery.LeaseExpiryMonitorWorker;

namespace Tracto.Orchestrator.Infrastructure.Persistence;

/// <summary>
/// DI registration helpers for the Orchestrator persistence layer.
/// </summary>
public static class PersistenceServiceExtensions
{
    /// <summary>
    /// Registers <see cref="IDbContextFactory{OrchestratorDbContext}"/>, repositories, and
    /// <see cref="IUnitOfWork"/> backed by the SQLite file in <paramref name="settings"/>.
    ///
    /// OrchestratorDbContext is registered as <em>Scoped</em> so that all repositories and
    /// the UnitOfWork within a single HTTP request (or manually created DI scope) share the
    /// same DbContext instance.  This is required for the UoW pattern to work correctly:
    /// repositories add tracked entities to the context's change tracker, and the UoW's
    /// CommitAsync flushes them all in one transaction.  A Transient context would give each
    /// dependency its own tracker, meaning CommitAsync would save nothing.
    ///
    /// IDbContextFactory remains Singleton (it holds only immutable DbContextOptions) so that
    /// non-scoped callers such as the /health endpoint can create their own context on demand
    /// without depending on a request scope.
    ///
    /// RestartRecoveryService is an IHostedService (resolved from the root container) and
    /// therefore cannot inject Scoped services directly.  It instead injects IServiceScopeFactory
    /// and creates a short-lived scope for its startup work.
    /// </summary>
    public static IServiceCollection AddOrchestratorPersistence(
        this IServiceCollection services,
        OrchestratorSettings settings)
    {
        var connectionString = $"Data Source={settings.DbPath}";

        services.AddSingleton<IDbContextFactory<OrchestratorDbContext>>(_ =>
        {
            var options = new DbContextOptionsBuilder<OrchestratorDbContext>()
                .UseSqlite(connectionString)
                .Options;
            return new OrchestratorDbContextFactory(options);
        });

        // Scoped: one shared instance per HTTP request (or per manually created DI scope).
        // All repositories and UnitOfWork resolved in the same scope share this instance.
        services.AddScoped<OrchestratorDbContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<OrchestratorDbContext>>().CreateDbContext());

        RegisterRepositoriesAndUoW(services);

        return services;
    }

    /// <summary>
    /// Registers an in-memory SQLite <see cref="IDbContextFactory{OrchestratorDbContext}"/> for unit
    /// and integration testing. Uses a named shared-cache connection so all contexts in the same
    /// process see the same schema.  Follows the same Scoped registration pattern as production so
    /// integration tests exercise the real UoW behaviour.
    /// </summary>
    public static IServiceCollection AddOrchestratorPersistenceInMemory(
        this IServiceCollection services,
        string databaseName = "tracto-test")
    {
        var connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared";

        services.AddSingleton<IDbContextFactory<OrchestratorDbContext>>(_ =>
        {
            var options = new DbContextOptionsBuilder<OrchestratorDbContext>()
                .UseSqlite(connectionString)
                .Options;
            return new OrchestratorDbContextFactory(options);
        });

        // Scoped: mirrors the production registration so tests exercise the same UoW semantics.
        services.AddScoped<OrchestratorDbContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<OrchestratorDbContext>>().CreateDbContext());

        RegisterRepositoriesAndUoW(services);

        return services;
    }

    /// <summary>
    /// Registers repositories and <see cref="IUnitOfWork"/> as Scoped services so they follow
    /// the lifetime of the shared <see cref="OrchestratorDbContext"/> within each DI scope.
    /// Extracted so both production and test registration paths share the same wiring.
    /// </summary>
    private static void RegisterRepositoriesAndUoW(IServiceCollection services)
    {
        services.AddScoped<IRunRepository, EfRunRepository>();
        services.AddScoped<IWorkerRepository, EfWorkerRepository>();
        services.AddScoped<IJournalRepository, EfJournalRepository>();
        services.AddScoped<IArtifactRepository, EfArtifactRepository>();
        services.AddScoped<IScheduleRepository, EfScheduleRepository>();
        // Register as the Application-layer contract so PullJobCommandHandler (and other
        // handlers) can inject ILeaseRepository from Application.Abstractions via DI.
        services.AddScoped<Application.Abstractions.ILeaseRepository, EfLeaseRepository>();
        // OC-06: Final Result Processing.
        services.AddScoped<Application.Abstractions.IFinalResultRepository, EfFinalResultRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        // OC-17: Security Controls — single class implements both interface contracts.
        services.AddScoped<EfSecurityEventLog>();
        services.AddScoped<Application.Abstractions.ISecurityEventLog>(
            sp => sp.GetRequiredService<EfSecurityEventLog>());
        services.AddScoped<Domain.Security.ISecurityEventLog>(
            sp => sp.GetRequiredService<EfSecurityEventLog>());
        // OC-07: Async Callback Intake and Replay Protection.
        services.AddScoped<ICallbackRepository, EfCallbackRepository>();
        services.AddScoped<Application.Abstractions.IHmacValidator, HmacValidator>();
        // OC-08: Continuation Mapping and Run B Creation.
        services.AddScoped<Application.Abstractions.IContinuationMappingRepository, EfContinuationMappingRepository>();
        services.AddScoped<Application.Abstractions.IContinuationRunRepository, EfContinuationRunRepository>();
        // OC-12: UNKNOWN Runs and Operator Resolution.
        services.AddScoped<Application.Abstractions.IResolutionRepository, EfResolutionRepository>();
        // OC-13: Admin and Operator API.
        // EfAdminJobRepository satisfies both IAdminJobRepository and IAdminRunQueryRepository.
        services.AddScoped<EfAdminJobRepository>();
        services.AddScoped<Application.Abstractions.IAdminJobRepository>(
            sp => sp.GetRequiredService<EfAdminJobRepository>());
        services.AddScoped<Application.Abstractions.IAdminRunQueryRepository>(
            sp => sp.GetRequiredService<EfAdminJobRepository>());
        // EfAdminSecurityEventRepository satisfies both IAdminSecurityEventRepository and ISecurityEventReader.
        services.AddScoped<Security.EfAdminSecurityEventRepository>();
        services.AddScoped<Application.Abstractions.IAdminSecurityEventRepository>(
            sp => sp.GetRequiredService<Security.EfAdminSecurityEventRepository>());
        services.AddScoped<Application.Abstractions.ISecurityEventReader>(
            sp => sp.GetRequiredService<Security.EfAdminSecurityEventRepository>());
        // EfAdminBundleSigner satisfies both IAdminBundleSigner and ITelemetryBundleSigner.
        services.AddScoped<Security.EfAdminBundleSigner>();
        services.AddScoped<Application.Abstractions.IAdminBundleSigner>(
            sp => sp.GetRequiredService<Security.EfAdminBundleSigner>());
        services.AddScoped<Application.Abstractions.ITelemetryBundleSigner>(
            sp => sp.GetRequiredService<Security.EfAdminBundleSigner>());
        // OC-14: Telemetry and Observability.
        services.AddScoped<Application.Abstractions.ITelemetryService, EfTelemetryService>();
        services.AddScoped<EfTelemetryRepository>();
        services.AddScoped<Domain.Telemetry.ITelemetryRepository>(
            sp => sp.GetRequiredService<EfTelemetryRepository>());
        services.AddScoped<Application.Abstractions.ITelemetryRepository>(
            sp => sp.GetRequiredService<EfTelemetryRepository>());
    }

    /// <summary>
    /// Registers OC-05 background services: the scoped <see cref="LeaseExpiryMonitorService"/>
    /// (unit-testable, takes repos directly) and its singleton background worker
    /// (<c>LeaseExpiryMonitorWorker</c>) that creates a new scope per polling tick.
    /// Call this from Program.cs after <see cref="AddOrchestratorPersistence"/>.
    /// </summary>
    public static IServiceCollection AddLeaseExpiryMonitor(
        this IServiceCollection services)
    {
        // Scoped so the worker resolves it inside a per-tick IServiceScope.
        services.AddScoped<AppLeaseExpiryMonitorService>();
        // Singleton background service; uses IServiceScopeFactory internally.
        services.AddHostedService<LeaseExpiryMonitorWorker>();
        return services;
    }

    /// <summary>
    /// Registers OC-10 background services: the scoped <see cref="ReconciliationService"/>
    /// (unit-testable, takes repos and handler directly) and its singleton background worker
    /// (<see cref="ReconciliationWorker"/>) that creates a new scope per polling tick.
    ///
    /// <see cref="ReconcileRunCommandHandler"/> must be registered separately (Transient)
    /// in Program.cs before calling this extension, as it is a dependency of
    /// <see cref="ReconciliationService"/>.
    ///
    /// Call this from Program.cs after <see cref="AddOrchestratorPersistence"/>.
    /// </summary>
    public static IServiceCollection AddReconciliationWorker(this IServiceCollection services)
    {
        // Scoped so the worker resolves it inside a per-tick IServiceScope.
        services.AddScoped<AppReconciliationService>();
        // Singleton background service; uses IServiceScopeFactory internally.
        services.AddHostedService<ReconciliationWorker>();
        return services;
    }
}
