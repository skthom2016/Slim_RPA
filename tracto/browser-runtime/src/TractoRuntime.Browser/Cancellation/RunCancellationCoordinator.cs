using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TractoRuntime.Browser.Context;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Lease;
using TractoRuntime.Browser.Worker;

namespace TractoRuntime.Browser.Cancellation;

/// <summary>
/// Implements <see cref="IRunCancellationCoordinator"/> for BRW-US-016 —
/// Cancellation and Resource Release.
///
/// <para><b>AC#1 — In-flight download and navigation cleanup</b><br/>
/// <see cref="CancelRunAsync"/> cancels the run's registered
/// <see cref="CancellationTokenSource"/>, which propagates the cancellation signal to
/// every awaiting operation:
/// <list type="bullet">
///   <item><c>FileDownloadHandler</c> catches <see cref="OperationCanceledException"/>,
///         deletes any partial file, and re-throws so the run terminates as CANCELLED.</item>
///   <item><c>NavigationExecutor</c> and <c>ActionExecutor</c> honour the token on every
///         awaited Playwright call.</item>
/// </list>
/// No partial sensitive content therefore remains at an exposed path after cancellation.</para>
///
/// <para><b>AC#2 — Bounded context and process release</b><br/>
/// After signalling, the coordinator waits up to <see cref="RunCancellationOptions.CleanupTimeout"/>
/// for the run executor to call <see cref="SignalCleanupComplete"/>.  If the timeout
/// expires, <see cref="ForceReleaseAsync"/> closes the browser context via
/// <see cref="IBrowserContextManager.ReleaseContextAsync"/>, ensuring no orphaned context
/// or process remains.</para>
///
/// <para><b>AC#3 — Lease and profile lock release</b><br/>
/// When a <c>leaseToken</c> was supplied at registration, the forced-cleanup path releases
/// the lease via <see cref="ILeaseManager.ReleaseAsync"/>.  The profile lock registered
/// in <see cref="IBrowserContextManager"/> is released implicitly when the context is
/// closed.</para>
///
/// <para><b>AC#4 — No orphaned contexts or exposed credentials</b><br/>
/// Both the normal path (run executor releases resources then calls
/// <see cref="SignalCleanupComplete"/>) and the forced path guarantee that the context
/// is closed, the lease is released, and the journal is closed with <c>CANCELLED</c>
/// before this method returns.</para>
/// </summary>
public sealed class RunCancellationCoordinator(
    IBrowserContextManager contextManager,
    ILeaseManager leaseManager,
    IRunJournal journal,
    RunCancellationOptions options,
    ILogger<RunCancellationCoordinator> logger)
    : IRunCancellationCoordinator
{
    private readonly ConcurrentDictionary<string, RunEntry> _runs =
        new(StringComparer.Ordinal);

    // ── IRunCancellationCoordinator ───────────────────────────────────────────

    /// <inheritdoc/>
    public void RegisterRun(
        string runId,
        CancellationTokenSource runCts,
        string? leaseToken = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(runCts);

        var entry = new RunEntry(runCts, leaseToken);

        if (!_runs.TryAdd(runId, entry))
            throw new InvalidOperationException(
                $"Run '{runId}' is already registered with the cancellation coordinator. " +
                "Ensure SignalCleanupComplete or CancelRunAsync was called for the previous attempt.");

        logger.LogDebug("Run {RunId} registered for cancellation tracking (hasLease={HasLease}).",
            runId, leaseToken is not null);
    }

    /// <inheritdoc/>
    public async Task<RunCancellationResult> CancelRunAsync(
        string runId,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        if (!_runs.TryGetValue(runId, out var entry))
        {
            logger.LogWarning(
                "CancelRunAsync: run {RunId} is not registered " +
                "(already completed or unknown — no action taken).", runId);

            return new RunCancellationResult
            {
                RunId                  = runId,
                CancellationSignalled  = false,
                CleanupCompletedInTime = false,
                CleanupDuration        = TimeSpan.Zero
            };
        }

        var sw = Stopwatch.StartNew();

        // AC#1: Signal cancellation — propagates through the CancellationToken hierarchy
        // to FileDownloadHandler, NavigationExecutor, ActionExecutor, and every awaited
        // Playwright call in the run.
        if (!entry.Cts.IsCancellationRequested)
        {
            await entry.Cts.CancelAsync().ConfigureAwait(false);
            logger.LogInformation("Cancellation signal sent for run {RunId}.", runId);
        }
        else
        {
            logger.LogDebug(
                "Cancellation for run {RunId} was already requested; " +
                "waiting for cleanup to complete.", runId);
        }

        // AC#2: Wait for bounded cleanup.
        // The run executor calls SignalCleanupComplete after it has released the context and lease.
        bool completedInTime;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(options.CleanupTimeout);

        try
        {
            await entry.CleanupComplete.Task
                .WaitAsync(timeoutCts.Token)
                .ConfigureAwait(false);

            completedInTime = true;
            logger.LogInformation(
                "Run {RunId} cleanup completed within bounded time ({ElapsedMs:F1} ms).",
                runId, sw.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            // Either the cleanup timeout fired OR the caller's ct was cancelled.
            // In both cases we must force-release resources so no orphaned browser
            // context, lease, or journal entry remains (AC#4).
            completedInTime = false;
            bool callerCancelled = ct.IsCancellationRequested;

            if (!callerCancelled)
            {
                // The cleanup timeout fired — log and force-release.
                logger.LogWarning(
                    "Run {RunId} cleanup did not complete within {Timeout}; " +
                    "performing forced resource release (BRW-US-016 AC#2).",
                    runId, options.CleanupTimeout);
            }
            else
            {
                // The caller's cancellation token fired (e.g. Orchestrator connection dropped).
                // We still force-release to prevent an orphaned context (AC#4), then rethrow.
                logger.LogWarning(
                    "CancelRunAsync was itself cancelled for run {RunId}; " +
                    "performing forced resource release to prevent orphaned browser context.",
                    runId);
            }

            await ForceReleaseAsync(runId, entry).ConfigureAwait(false);

            if (callerCancelled)
            {
                // Clean up the entry before propagating so IsRunActive returns false
                // and a subsequent RegisterRun call for the same runId is accepted.
                sw.Stop();
                _runs.TryRemove(runId, out _);
                ct.ThrowIfCancellationRequested();
            }
        }

        sw.Stop();

        // Remove the entry — the run is now fully terminated (normal or forced path).
        _runs.TryRemove(runId, out _);

        return new RunCancellationResult
        {
            RunId                  = runId,
            CancellationSignalled  = true,
            CleanupCompletedInTime = completedInTime,
            CleanupDuration        = sw.Elapsed
        };
    }

    /// <inheritdoc/>
    public void SignalCleanupComplete(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        if (_runs.TryGetValue(runId, out var entry))
        {
            // TrySetResult is idempotent — safe if called more than once.
            entry.CleanupComplete.TrySetResult();
            logger.LogDebug("Run {RunId} signalled cleanup complete.", runId);
        }
        else
        {
            // The forced-cleanup path may have already removed the entry; this is not an error.
            logger.LogDebug(
                "SignalCleanupComplete: run {RunId} was not registered " +
                "(forced cleanup may have already removed it).", runId);
        }
    }

    /// <inheritdoc/>
    public bool IsRunActive(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        return _runs.ContainsKey(runId);
    }

    // ── Forced cleanup (AC#2, AC#3, AC#4) ────────────────────────────────────

    /// <summary>
    /// Forcibly releases browser context, lease, and journal after the bounded cleanup
    /// timeout expired.  Uses <see cref="CancellationToken.None"/> for all cleanup calls
    /// so that cancellation of the <em>cancel request</em> does not abort the release.
    /// Errors in individual steps are logged and swallowed to ensure all cleanup steps
    /// are attempted regardless of partial failures.
    /// </summary>
    private async Task ForceReleaseAsync(string runId, RunEntry entry)
    {
        // AC#2: Force-close the browser context and release all owned pages and processes.
        try
        {
            await contextManager
                .ReleaseContextAsync(runId, CancellationToken.None)
                .ConfigureAwait(false);

            logger.LogInformation(
                "Forced release: browser context closed for run {RunId} (BRW-US-016 AC#2).", runId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Forced release: error closing browser context for run {RunId}.", runId);
        }

        // AC#3: Release the Orchestrator lease so the worker is available for new jobs.
        if (entry.LeaseToken is { } leaseToken)
        {
            try
            {
                await leaseManager
                    .ReleaseAsync(leaseToken, CancellationToken.None)
                    .ConfigureAwait(false);

                logger.LogInformation(
                    "Forced release: lease released for run {RunId} (BRW-US-016 AC#3).", runId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Forced release: error releasing lease for run {RunId}.", runId);
            }
        }

        // Close the journal with CANCELLED so that ReconciliationService knows the run ended.
        try
        {
            await journal
                .CloseAsync(runId, RunState.CANCELLED, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Forced release: error closing journal for run {RunId}.", runId);
        }
    }

    // ── Private entry ─────────────────────────────────────────────────────────

    /// <summary>Per-run tracking entry held by the coordinator.</summary>
    private sealed record RunEntry(CancellationTokenSource Cts, string? LeaseToken)
    {
        /// <summary>
        /// Completed by <see cref="RunCancellationCoordinator.SignalCleanupComplete"/> when
        /// the run executor has finished releasing all run-owned resources.
        /// </summary>
        public TaskCompletionSource CleanupComplete { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
