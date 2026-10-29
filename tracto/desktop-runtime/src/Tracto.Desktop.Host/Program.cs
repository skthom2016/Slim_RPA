#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tracto.Contract.Models;
using Tracto.Contract.Models.Execution;
using Tracto.Contract.Models.Protocol;
using Tracto.Contract.Orchestrator;
using Tracto.Desktop.Actions;
using Tracto.Desktop.Actions.Handlers;
using Tracto.Desktop.AppLifecycle;
using Tracto.Desktop.Host;
using Tracto.Desktop.Protocol;

ProcessDpi.EnableAwareness();

// ── Configuration from environment ───────────────────────────────────────────
string orchestratorUrl = Environment.GetEnvironmentVariable("ORCHESTRATOR_URL") ?? "http://localhost:5080";
string workerId        = Environment.GetEnvironmentVariable("WORKER_ID")        ?? "desktop-runtime-001";
string workerVersion   = Environment.GetEnvironmentVariable("WORKER_VERSION")   ?? "1.0.0";
string sessionId       = Environment.GetEnvironmentVariable("INTERACTIVE_SESSION_ID")
                         ?? Environment.GetEnvironmentVariable("SESSIONNAME")
                         ?? "Console";

Console.WriteLine($"[Desktop Host] Starting — Orchestrator: {orchestratorUrl}  Worker: {workerId}");

// ── Build host ────────────────────────────────────────────────────────────────
var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(b => b.SetMinimumLevel(LogLevel.Information))
    .ConfigureServices(services =>
    {
        // ── HTTP clients ──────────────────────────────────────────────────────
        services.AddHttpClient("orchestrator", client =>
        {
            client.BaseAddress = new Uri(orchestratorUrl.TrimEnd('/') + "/");
            client.Timeout     = TimeSpan.FromSeconds(30);
        });
        // General-purpose client — used by the script engine and http.get handler.
        services.AddHttpClient("general", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        // ── Orchestrator bridge ───────────────────────────────────────────────
        services.AddSingleton<HttpOrchestratorBridge>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("orchestrator");
            return new HttpOrchestratorBridge(http, "desktop", sessionId);
        });

        // ── Desktop infrastructure ────────────────────────────────────────────
        services.AddSingleton<IAppLifecycleManager, WindowsAppLifecycleManager>();

        // ── Action handlers ───────────────────────────────────────────────────
        // Handlers generated from recordings (generic desktop events):
        services.AddSingleton<IActionHandler, ClickHandler>();
        services.AddSingleton<IActionHandler, TypeHandler>();
        services.AddSingleton<IActionHandler, KeyHandler>();
        services.AddSingleton<IActionHandler, NavigateHandler>();
        services.AddSingleton<IActionHandler, FocusHandler>();
        services.AddSingleton<IActionHandler, ScrollHandler>();
        services.AddSingleton<IActionHandler, CatchAllHandler>();
        services.AddSingleton<IActionHandler, DelayHandler>();
        // Existing specific handlers:
        services.AddSingleton<IActionHandler, AppLaunchHandler>();
        services.AddSingleton<IActionHandler, NotepadTypeHandler>();
        services.AddSingleton<IActionHandler, FileSaveAsHandler>();
        services.AddSingleton<IActionHandler, HttpGetHandler>();

        // ActionDispatcher collects all IActionHandler registrations.
        services.AddSingleton<ActionDispatcher>();

        // ── Script execution engine ───────────────────────────────────────────
        services.AddSingleton<ScriptExecutionEngine>(sp =>
        {
            var dispatcher = sp.GetRequiredService<ActionDispatcher>();
            var http       = sp.GetRequiredService<IHttpClientFactory>().CreateClient("general");
            var log        = sp.GetRequiredService<ILogger<ScriptExecutionEngine>>();
            return new ScriptExecutionEngine(dispatcher, http, log);
        });

        // ── Host config + worker ──────────────────────────────────────────────
        services.AddSingleton(new HostConfig(workerId, workerVersion, sessionId));
        services.AddHostedService<DesktopRuntimeWorker>();
    })
    .Build();

await host.RunAsync();

// ── Configuration record ──────────────────────────────────────────────────────
internal sealed record HostConfig(string WorkerId, string WorkerVersion, string SessionId);

// ── Hosted service — registration + pull–execute loop ────────────────────────
internal sealed class DesktopRuntimeWorker : BackgroundService
{
    private readonly HttpOrchestratorBridge  _orch;
    private readonly ScriptExecutionEngine   _engine;
    private readonly HostConfig              _cfg;
    private readonly ILogger<DesktopRuntimeWorker> _log;

    public DesktopRuntimeWorker(
        HttpOrchestratorBridge orch,
        ScriptExecutionEngine engine,
        HostConfig cfg,
        ILogger<DesktopRuntimeWorker> log)
    {
        _orch   = orch;
        _engine = engine;
        _cfg    = cfg;
        _log    = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // ── Register ──────────────────────────────────────────────────────────
        var registrar = new DesktopWorkerRegistrar(_cfg.WorkerId, _cfg.WorkerVersion, _cfg.SessionId);
        RegistrationResult reg;
        try
        {
            reg = await registrar.RegisterAsync(_orch, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Registration request failed — is the Orchestrator running at {Url}?",
                Environment.GetEnvironmentVariable("ORCHESTRATOR_URL") ?? "http://localhost:5080");
            return;
        }

        if (!reg.IsAccepted)
        {
            _log.LogError("Registration refused: {Code} — {Message}", reg.ErrorCode, reg.Message);
            return;
        }

        _log.LogInformation("Worker registered — workerId: {WorkerId}", reg.WorkerId);

        // ── Pull–execute loop ─────────────────────────────────────────────────
        var pullLoop  = new JobPullLoop(_cfg.WorkerId, pollIntervalMs: 5_000);
        var submitter = new FinalResultSubmitter(_cfg.WorkerId);

        while (!ct.IsCancellationRequested)
        {
            JobContext job;
            try
            {
                job = await pullLoop.PullJobAsync(_orch, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Pull failed — retrying in 5 s");
                await Task.Delay(5_000, ct);
                continue;
            }

            _log.LogInformation("Job acquired — runId: {RunId}", job.RunId);

            using var jobCts      = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var heartbeatTask = HeartbeatLoop.RunAsync(
                _orch, _cfg.WorkerId, job.LeaseId, intervalMs: 15_000, jobCts.Token,
                onStopRequested: () =>
                {
                    _log.LogWarning("Run {RunId}: orchestrator cancelled the run or the lease was lost — stopping", job.RunId);
                    jobCts.Cancel();
                });

            var startedAt = DateTimeOffset.UtcNow;
            RunState runState;
            List<StepResult> steps;

            try
            {
                (runState, steps) = await _engine.RunAsync(job, jobCts.Token);
            }
            catch (OperationCanceledException)
            {
                runState = RunState.Cancelled;
                steps    = [];
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unhandled error in run {RunId}", job.RunId);
                runState = RunState.Unknown;
                steps    = [];
            }
            finally
            {
                await jobCts.CancelAsync();
                try { await heartbeatTask; }
                catch (Exception ex) { _log.LogWarning(ex, "Heartbeat loop ended with an error"); }
            }

            try
            {
                await submitter.SubmitAsync(
                    _orch, job, runState, steps,
                    unknownReason: runState == RunState.Unknown ? "UnhandledHostException" : null,
                    startedAt: startedAt,
                    ct: ct);

                _log.LogInformation("Run {RunId} complete — state: {State}", job.RunId, runState);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to submit result for run {RunId}", job.RunId);
            }
        }
    }
}
