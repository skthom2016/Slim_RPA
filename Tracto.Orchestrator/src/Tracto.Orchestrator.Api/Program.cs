using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using System.Text;
using System.Text.Json.Serialization;
using Tracto.Orchestrator.Api.Admin;
using Tracto.Orchestrator.Api.Artifacts;
using Tracto.Orchestrator.Api.Callbacks;
using Tracto.Orchestrator.Api.ContinuationMappings;
using Tracto.Orchestrator.Api.Health;
using Tracto.Orchestrator.Api.Jobs;
using Tracto.Orchestrator.Api.Schedules;
using Tracto.Orchestrator.Api.Security;
using Tracto.Orchestrator.Api.Telemetry;
using Tracto.Orchestrator.Api.Workers;
using Tracto.Orchestrator.Infrastructure.Telemetry;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Application.Admin;
using Tracto.Orchestrator.Application.Artifacts;
using Tracto.Orchestrator.Application.Callbacks;
using Tracto.Orchestrator.Application.Jobs;
using Tracto.Orchestrator.Application.Recovery;
using AdminResolveHandler   = Tracto.Orchestrator.Application.Admin.ResolveUnknownRunCommandHandler;
using OC12ResolveHandler    = Tracto.Orchestrator.Application.Jobs.ResolveUnknownRunCommandHandler;
using Tracto.Orchestrator.Application.Security;
using Tracto.Orchestrator.Application.Workers;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Domain.Security;
using Tracto.Orchestrator.Domain.Settings;
using Tracto.Orchestrator.Infrastructure.Deployment;
using Tracto.Orchestrator.Infrastructure.Persistence;
using Tracto.Orchestrator.Infrastructure.Security;
using SystemClock             = Tracto.Orchestrator.Infrastructure.Clock.SystemClock;
using AppJobsReconcileRunHandler = Tracto.Orchestrator.Application.Jobs.ReconcileRunCommandHandler;

// ── Bootstrap logger — captures startup failures before full Serilog initialises ──
// CreateLogger() (not CreateBootstrapLogger()) avoids the ReloadableLogger.Freeze()
// exception that fires when multiple WebApplicationFactory instances start in the
// same process during integration tests.  In production both behave identically.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try
{
    // ── 1. Build the host first so IConfiguration includes UseSetting overrides ──
    // Reading TRACTO_MODE from builder.Configuration (not Environment.GetEnvironmentVariable)
    // means WebApplicationFactory.ConfigureWebHost can override it via builder.UseSetting(...)
    // without polluting the process-wide environment — critical for parallel integration tests.
    var builder = WebApplication.CreateBuilder(args);

    // ── JSON: string-enum support for all minimal-API endpoints ─────────────────
    // Without this, ASP.NET Core's default System.Text.Json requires integer values for
    // enum properties (e.g. TriggerType). Adding JsonStringEnumConverter here means
    // "Manual", "Scheduled", etc. deserialise correctly from JSON request bodies.
    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

    // ── 2. Parse TRACTO_MODE — prefer IConfiguration (includes UseSetting), fall back to env var.
    var rawMode = builder.Configuration[DeploymentModeResolver.EnvVarName]
                  ?? Environment.GetEnvironmentVariable(DeploymentModeResolver.EnvVarName);
    var modeResult = DeploymentModeResolver.Resolve(rawMode);
    if (modeResult.IsFailure)
    {
        Log.Fatal(
            "Orchestrator startup aborted: {ErrorCode} — {ErrorMessage}",
            modeResult.Error.Code,
            modeResult.Error.Message);
        // Throw so WebApplicationFactory-based integration tests observe the failure as an exception.
        throw new InvalidOperationException(
            $"[{modeResult.Error.Code}] {modeResult.Error.Message}");
    }

    var mode = modeResult.Value;

    // ── 3. Serilog structured logging ────────────────────────────────────────────
    // preserveStaticLogger: true prevents Freeze() from being called on Log.Logger
    // when a second WebApplicationFactory starts in the same process (integration tests).
    builder.Host.UseSerilog(
        (ctx, services, cfg) =>
        {
            cfg.ReadFrom.Configuration(ctx.Configuration)
               .ReadFrom.Services(services)
               .Enrich.FromLogContext()
               .Enrich.WithProperty("OrchestratorMode", mode.ToString())
               .WriteTo.Console(
                   outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");
        },
        preserveStaticLogger: true);

    // ── 4. Build OrchestratorSettings from appsettings + mode ────────────────────
    var settingsSection = builder.Configuration.GetSection("Orchestrator");
    var settings = new OrchestratorSettings(
        Mode:            mode,
        ApiPort:         settingsSection.GetValue<int?>("ApiPort") ?? 8080,
        DbPath:          settingsSection["DbPath"] ?? "tracto-orchestrator.db",
        TlsCertPath:     settingsSection["TlsCertPath"],
        AllowedCidr:     settingsSection["AllowedCidr"],
        AllowedAdminCidr: settingsSection["AllowedAdminCidr"],
        JwtSecret:       settingsSection["JwtSecret"],
        HmacKeys:        settingsSection.GetSection("HmacKeys")
                             .GetChildren()
                             .Where(c => !string.IsNullOrEmpty(c.Value))
                             .ToDictionary(c => c.Key, c => c.Value!));

    // Bind on 0.0.0.0 (Connected) vs 127.0.0.1 (Isolated) — invariant from OC-16.
    builder.WebHost.UseUrls(settings.ListenUrl);

    // ── 5. DI registrations ──────────────────────────────────────────────────────
    // EF Core + SQLite IDbContextFactory, repositories, and UnitOfWork.
    builder.Services.AddOrchestratorPersistence(settings);

    // Expose settings for injection into handlers and middleware.
    builder.Services.AddSingleton(settings);

    // OC-15: production clock (DateTimeOffset.UtcNow).
    builder.Services.AddSingleton<IClock, SystemClock>();

    // OC-15: restart-recovery hosted service — runs once at startup.
    builder.Services.AddHostedService<RestartRecoveryService>();

    // OC-11: Artifact Registry application-layer handlers.
    builder.Services.AddTransient<PublishArtifactHandler>();
    builder.Services.AddTransient<GetArtifactHandler>();
    builder.Services.AddTransient<DeleteArtifactHandler>();

    // OC-01: Worker registration and capability advertisement handlers.
    builder.Services.AddTransient<RegisterWorkerCommandHandler>();
    builder.Services.AddTransient<MarkWorkerOfflineCommandHandler>();

    // OC-02: Job Intake handlers.
    builder.Services.AddTransient<CreateJobCommandHandler>();
    builder.Services.AddTransient<GetJobQueryHandler>();
    builder.Services.AddTransient<CreateScheduleCommandHandler>();

    // OC-03: Queue Management / Worker Capability Matching.
    builder.Services.AddTransient<PullJobCommandHandler>();

    // OC-04: Lease Acceptance and AttemptId Fencing.
    builder.Services.AddTransient<AcceptLeaseCommandHandler>();

    // OC-05: Heartbeat Processing and Lease Expiry.
    builder.Services.AddTransient<SendHeartbeatCommandHandler>();

    // OC-09: Cancellation.
    builder.Services.AddTransient<CancelRunCommandHandler>();

    // OC-12: UNKNOWN Runs and Operator Resolution.
    // OC12ResolveHandler (Application.Jobs) handles POST /admin/jobs/{runId}/resolve.
    builder.Services.AddTransient<GetUnknownRunDetailQueryHandler>();
    builder.Services.AddTransient<OC12ResolveHandler>();

    // OC-06: Final Result Processing and AttemptId Commit.
    builder.Services.AddTransient<CommitFinalResultCommandHandler>();

    // OC-07: Async Callback Ingestion.
    builder.Services.AddTransient<IngestCallbackCommandHandler>();
    // IContinuationQueue: in-process implementation that creates Run B in a fresh DI scope,
    // avoiding nested-transaction conflicts with the callback handler's outer transaction.
    builder.Services.AddSingleton<Tracto.Orchestrator.Application.Abstractions.IContinuationQueue,
                                   Tracto.Orchestrator.Infrastructure.Services.ScopedContinuationQueue>();

    // OC-08: Continuation Mapping and Run B Creation.
    builder.Services.AddTransient<CreateContinuationMappingCommandHandler>();
    builder.Services.AddTransient<GetContinuationMappingQueryHandler>();
    builder.Services.AddTransient<CreateContinuationRunCommandHandler>();
    builder.Services.AddTransient<StaticSucceededContinuationService>();

    // OC-10: Reconciliation After Disconnect or Restart — handler in Application.Jobs namespace.
    builder.Services.AddTransient<AppJobsReconcileRunHandler>();
    // LeaseExpiryMonitorService (Scoped) + LeaseExpiryMonitorWorker (Singleton HostedService).
    builder.Services.AddLeaseExpiryMonitor();

    // OC-10: ReconciliationService (Scoped) + ReconciliationWorker (Singleton HostedService).
    builder.Services.AddReconciliationWorker();

    // OC-02: CronTriggerService stub — logs "CronTriggerService running" on start.
    builder.Services.AddHostedService<CronTriggerService>();

    // OC-13: Admin and Operator API.
    // AdminResolveHandler (Application.Admin) handles POST /admin/jobs/{runId}/force-resolve.
    builder.Services.AddTransient<ListAdminJobsQueryHandler>();
    builder.Services.AddTransient<GetAdminJobDetailQueryHandler>();
    builder.Services.AddTransient<ExportTelemetryBundleQueryHandler>();
    builder.Services.AddTransient<AdminResolveHandler>();
    // OC-13: Admin upsert for continuation mappings + admin manual job trigger.
    builder.Services.AddTransient<UpsertAdminContinuationMappingCommandHandler>();

    // OC-14: Telemetry and Observability — query handler for GET /telemetry/{runId}.
    builder.Services.AddTransient<Tracto.Orchestrator.Application.Telemetry.GetTelemetryByRunIdQueryHandler>();

    // OC-17: Security Controls — SecretScanService (stateless, safe as singleton).
    // EfSecurityEventLog middleware needs a Singleton-lifetime ISecurityEventLog so that
    // conventional middleware (created once from the root DI container) can resolve it.
    // AddOrchestratorPersistence registers a Scoped EfSecurityEventLog for request handlers;
    // this Singleton forwards the same contract to a root-resolvable instance for middleware.
    builder.Services.AddSingleton<Tracto.Orchestrator.Domain.Security.ISecurityEventLog, EfSecurityEventLog>();
    builder.Services.AddSingleton<SecretScanService>();

    // Build JWT auth options from settings (null key → parse-only, no signature check).
    var jwtOptions = new JwtAuthOptions
    {
        SigningKey = string.IsNullOrWhiteSpace(settings.JwtSecret)
            ? null
            : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.JwtSecret)),
        ValidateIssuerAndAudience = false,
    };
    builder.Services.AddSingleton(jwtOptions);

    // ── 6. OpenTelemetry tracing skeleton ────────────────────────────────────────
    // Swap the Console exporter for OpenTelemetry.Exporter.OpenTelemetryProtocol in production.
    // OC-14: OrchestratorActivitySource is added so all command spans are captured.
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource
            .AddService(
                serviceName: OrchestratorActivitySource.SourceName,
                serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0"))
        .WithTracing(tracing => tracing
            .AddSource(OrchestratorActivitySource.SourceName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddConsoleExporter());

    // ── 7. Build + configure the pipeline ────────────────────────────────────────
    var app = builder.Build();

    // OC-15: apply EF migrations on startup (idempotent, restart-safe).
    // In the Testing environment (integration tests with in-memory SQLite) use
    // EnsureCreatedAsync — MigrateAsync cannot apply versioned migrations on
    // anonymous in-memory connections because each new connection opens a fresh DB.
    // In all other environments MigrateAsync is authoritative.
    try
    {
        using var scope = app.Services.CreateScope();
        var factory = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<OrchestratorDbContext>>();
        await using var db = await factory.CreateDbContextAsync();

        if (app.Environment.IsEnvironment("Testing"))
        {
            await db.Database.EnsureCreatedAsync();
            Log.Information("Database schema ensured (Testing environment).");
        }
        else
        {
            await db.Database.MigrateAsync();
            Log.Information("Database migrations applied successfully.");
        }
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "Failed to initialise database. Orchestrator will not start.");
        return;
    }

    app.UseSerilogRequestLogging();

    // ── OC-17: Security middleware pipeline ───────────────────────────────────
    // Order matters: AuditLogging first (capture everything), then JWT, then IP allowlist.
    app.UseMiddleware<AuditLoggingMiddleware>();
    app.UseMiddleware<JwtAuthMiddleware>();
    app.UseMiddleware<AdminIpAllowlistMiddleware>();

    // ── OC-11: Artifact Registry endpoints ───────────────────────────────────────
    app.MapArtifactEndpoints();

    // ── OC-01: Worker registration endpoints ─────────────────────────────────────
    app.MapWorkerEndpoints();

    // ── OC-02: Job Intake endpoints ───────────────────────────────────────────────
    app.MapJobEndpoints();

    // ── OC-04: Lease Acceptance endpoints ────────────────────────────────────────
    app.MapLeaseEndpoints();

    // ── OC-09: Cancellation endpoint ──────────────────────────────────────────────
    app.MapCancelEndpoints();

    // ── OC-06: Final Result Processing endpoint ───────────────────────────────
    app.MapResultEndpoints();

    // ── OC-07: Async Callback Ingestion endpoint ──────────────────────────────────
    app.MapCallbackEndpoints();

    // ── OC-10: Reconciliation endpoint ───────────────────────────────────────────
    app.MapReconcileEndpoints();

    // ── OC-08: Continuation Mapping endpoints ────────────────────────────────────
    app.MapContinuationMappingEndpoints();

    // ── OC-02: Schedule Registry endpoints ───────────────────────────────────────
    app.MapScheduleEndpoints();

    // ── OC-14: Telemetry export endpoint ─────────────────────────────────────────
    app.MapTelemetryEndpoints();

    // ── OC-12: UNKNOWN Runs and Operator Resolution endpoints ────────────────────
    app.MapUnknownRunEndpoints();

    // ── OC-13: Admin and Operator API endpoints ───────────────────────────────────
    app.MapAdminEndpoints();

    // ── GET /health ───────────────────────────────────────────────────────────────
    // Returns { status, dbConnected, queueDepth, mode }.
    // dbConnected reflects a live SQLite ping; queueDepth is the count of QUEUED runs.
    app.MapGet("/health", async (IDbContextFactory<OrchestratorDbContext> dbFactory) =>
    {
        var dbConnected = false;
        var queueDepth = 0;

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            dbConnected = await db.Database.CanConnectAsync();
            if (dbConnected)
            {
                queueDepth = await db.RunRecords
                    .CountAsync(r => r.State == RunState.Queued);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Health check: database connectivity probe failed.");
        }

        return Results.Ok(new HealthResponse(
            Status: "healthy",
            DbConnected: dbConnected,
            QueueDepth: queueDepth,
            Mode: settings.Mode.ToString()));
    })
    .WithName("GetHealth")
    .WithTags("Monitoring")
    .Produces<HealthResponse>(StatusCodes.Status200OK);

    Log.Information(
        "Tracto Orchestrator starting — Mode={Mode} ListenUrl={ListenUrl} DbPath={DbPath}",
        settings.Mode,
        settings.ListenUrl,
        settings.DbPath);

    app.Run();
}
catch (Exception ex) when (ex is not OperationCanceledException && ex is not HostAbortedException)
{
    Log.Fatal(ex, "Tracto Orchestrator terminated unexpectedly during startup.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// Expose Program for WebApplicationFactory<Program> in integration tests.
public partial class Program { }
