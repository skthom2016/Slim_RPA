using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace TractoRuntime.Browser.Host;

/// <summary>
/// Worker loop: register with the Orchestrator, pull one job at a time, keep the lease
/// alive with heartbeats while the engine runs the script, then submit the result.
/// </summary>
public sealed class BrowserWorker(
    BrowserHostOptions options,
    OrchestratorClient orchestrator,
    BrowserRunEngine engine,
    ILogger<BrowserWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (options.AutoInstallBrowser && string.IsNullOrEmpty(options.Channel))
            EnsureBundledChromium();

        try
        {
            await orchestrator.RegisterAsync(options.WorkerId, StepTranslator.RuntimeActionNames, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Registration failed — is the Orchestrator running at {Url}?", options.OrchestratorUrl);
            return;
        }

        log.LogInformation("Worker {WorkerId} registered as runtimeType=browser ({Mode}, {Channel}).",
            options.WorkerId, options.Mode, string.IsNullOrEmpty(options.Channel) ? "bundled Chromium" : options.Channel);

        while (!ct.IsCancellationRequested)
        {
            JobLease? lease;
            try
            {
                lease = await orchestrator.PullAsync(options.WorkerId, options.InteractiveSessionId, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Pull failed — retrying in {Ms} ms", options.PollIntervalMs);
                await DelaySafe(options.PollIntervalMs, ct).ConfigureAwait(false);
                continue;
            }

            if (lease is null)
            {
                await DelaySafe(options.PollIntervalMs, ct).ConfigureAwait(false);
                continue;
            }

            await RunJobAsync(lease, ct).ConfigureAwait(false);
        }
    }

    private async Task RunJobAsync(JobLease lease, CancellationToken ct)
    {
        log.LogInformation("Job acquired — runId {RunId}, artifact {Artifact}/{Version}",
            lease.RunId, lease.ArtifactId, lease.ArtifactVersion);

        try
        {
            await orchestrator.AcceptLeaseAsync(lease, options.WorkerId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Lease accept failed for run {RunId}; skipping.", lease.RunId);
            return;
        }

        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        jobCts.CancelAfter(options.RunTimeoutMs);
        var timedOut = false;

        var heartbeat = HeartbeatLoopAsync(lease, jobCts);

        RunOutcome outcome;
        try
        {
            outcome = await engine.RunAsync(lease, jobCts.Token).ConfigureAwait(false);
            if (outcome.TerminalState == "Cancelled" && !ct.IsCancellationRequested && jobCts.IsCancellationRequested)
                timedOut = !_cancelRequestedByOrchestrator;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Unhandled error in run {RunId}", lease.RunId);
            outcome = new RunOutcome("Unknown", "INTERNAL", "UnhandledHostException", [], new Dictionary<string, string>());
        }
        finally
        {
            await jobCts.CancelAsync().ConfigureAwait(false);
            try { await heartbeat.ConfigureAwait(false); }
            catch (Exception ex) { log.LogDebug(ex, "Heartbeat loop ended with an error."); }
        }

        if (timedOut)
            outcome = outcome with { TerminalState = "TimedOut", ErrorCategory = "TIMEOUT", Message = "Run timeout reached." };

        try
        {
            await orchestrator.SubmitResultAsync(
                lease,
                outcome.TerminalState,
                outcome.ErrorCategory,
                outcome.UnknownReason,
                BrowserRunEngine.BuildResultPayload(outcome),
                outcome.Outputs,
                CancellationToken.None).ConfigureAwait(false);

            log.LogInformation("Run {RunId} complete — {State}{Detail}",
                lease.RunId, outcome.TerminalState, outcome.Message is null ? string.Empty : $": {outcome.Message}");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to submit result for run {RunId}", lease.RunId);
        }

        _cancelRequestedByOrchestrator = false;
    }

    private bool _cancelRequestedByOrchestrator;

    private async Task HeartbeatLoopAsync(JobLease lease, CancellationTokenSource jobCts)
    {
        long seq = 0;
        var failures = 0;

        while (!jobCts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(options.HeartbeatIntervalMs, jobCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }

            try
            {
                var stop = await orchestrator.HeartbeatAsync(lease, options.WorkerId, ++seq, jobCts.Token).ConfigureAwait(false);
                failures = 0;
                if (stop)
                {
                    log.LogWarning("Run {RunId}: Orchestrator requested cancellation.", lease.RunId);
                    _cancelRequestedByOrchestrator = true;
                    await jobCts.CancelAsync().ConfigureAwait(false);
                    return;
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                failures++;
                log.LogWarning(ex, "Heartbeat {Seq} failed ({Failures}/3).", seq, failures);
                if (failures >= 3)
                {
                    log.LogError("Run {RunId}: lease lost — stopping the run.", lease.RunId);
                    _cancelRequestedByOrchestrator = true;
                    await jobCts.CancelAsync().ConfigureAwait(false);
                    return;
                }
            }
        }
    }

    private void EnsureBundledChromium()
    {
        try
        {
            log.LogInformation("Checking the bundled Chromium (installs it on first start)...");
            var exit = Microsoft.Playwright.Program.Main(["install", "chromium"]);
            if (exit != 0)
                log.LogWarning("Playwright install returned exit code {Code}; launches may fail.", exit);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Playwright install could not run; launches may fail.");
        }
    }

    private static async Task DelaySafe(int ms, CancellationToken ct)
    {
        try { await Task.Delay(ms, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { /* shutting down */ }
    }
}
