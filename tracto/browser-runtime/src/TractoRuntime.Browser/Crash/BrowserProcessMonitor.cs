using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Worker;

namespace TractoRuntime.Browser.Crash;

/// <summary>
/// Production implementation of <see cref="IBrowserProcessMonitor"/>.
///
/// Satisfies BRW-US-004 (BRW-004, BRE-004):
/// <list type="bullet">
///   <item>
///     AC#1 — On any terminal state, all browser-owned processes are terminated.
///             Handled by <see cref="BrowserContextManager"/> <c>DisposeAsync</c>; this monitor
///             does not duplicate that responsibility.
///   </item>
///   <item>
///     AC#2 — When the browser crashes mid-run, the crash is recorded in the run journal and
///             no orphaned worker-owned browser process is left.  Playwright automatically marks
///             the <c>IBrowser</c> as disconnected; the handler records the crash entry and fires
///             the event so the worker can release all owned resources.
///   </item>
///   <item>
///     AC#3 — Crash before any irreversible action → terminal state is <c>FAILED</c>.
///   </item>
///   <item>
///     AC#4 — Crash with an in-flight effectful action → terminal state is <c>UNKNOWN</c>.
///             Crash after all effectful actions settled → <c>FAILED</c>.
///             The runtime does not blindly restart or re-execute steps.
///   </item>
/// </list>
/// </summary>
public sealed class BrowserProcessMonitor(
    IRunJournal journal,
    ILogger<BrowserProcessMonitor> logger)
    : IBrowserProcessMonitor
{
    // Tracks which runs have active crash monitoring
    private readonly ConcurrentDictionary<string, MonitoredRun> _monitored =
        new(StringComparer.Ordinal);

    private bool _disposed;

    // ── IBrowserProcessMonitor ────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Attach(IBrowser browser, string runId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var run = new MonitoredRun(browser, runId);

        if (!_monitored.TryAdd(runId, run))
        {
            logger.LogWarning(
                "BrowserProcessMonitor: run {RunId} is already being monitored; ignoring duplicate Attach.",
                runId);
            return;
        }

        // Playwright fires IBrowser.Disconnected when the underlying browser process exits,
        // whether cleanly or via crash.  The Suppress flag distinguishes the two cases.
        browser.Disconnected += (_, _) => HandleDisconnected(runId);

        logger.LogDebug(
            "Browser process crash monitor attached for run {RunId}.", runId);
    }

    /// <inheritdoc/>
    public void Detach(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        // Mark as suppressed BEFORE the Disconnected event can fire from a normal close.
        if (_monitored.TryGetValue(runId, out var run))
        {
            run.Suppress = true;
            _monitored.TryRemove(runId, out _);
            logger.LogDebug(
                "Browser process crash monitor detached for run {RunId} (clean shutdown).", runId);
        }
    }

    /// <inheritdoc/>
    public event EventHandler<CrashClassifiedEventArgs>? CrashClassified;

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        _disposed = true;
        _monitored.Clear();
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called synchronously on the Playwright event thread; dispatches async work to the
    /// thread-pool to avoid blocking Playwright's internal event loop.
    /// </summary>
    private void HandleDisconnected(string runId)
    {
        // Fire-and-forget: async processing on thread-pool; exceptions are logged internally.
        _ = Task.Run(() => ProcessCrashAsync(runId));
    }

    private async Task ProcessCrashAsync(string runId)
    {
        // Check if this disconnection is from a clean, planned shutdown
        if (!_monitored.TryGetValue(runId, out var run) || run.Suppress)
        {
            logger.LogDebug(
                "Disconnected event for run {RunId} ignored — monitor detached or suppressed "
                + "(expected clean shutdown).", runId);
            return;
        }

        logger.LogError(
            "Unexpected browser process disconnection detected for run {RunId}. "
            + "Recording crash and classifying terminal state.", runId);

        // AC#2: record the crash event in the run journal before classifying
        await RecordCrashEntryAsync(runId).ConfigureAwait(false);

        // AC#3 / AC#4: classify terminal state from what the journal shows
        var terminalState = await ClassifyTerminalStateAsync(runId).ConfigureAwait(false);

        var reason = terminalState == RunState.UNKNOWN
            ? "Browser process crashed with at least one effectful action of indeterminate outcome. "
              + "The attempt is terminated as UNKNOWN; no automatic restart will be performed."
            : "Browser process crashed; no effectful action had an indeterminate outcome. "
              + "The attempt is terminated as FAILED.";

        logger.LogError(
            "Run {RunId} terminal state after crash: {State}. {Reason}",
            runId, terminalState, reason);

        // Notify the worker — the worker is responsible for releasing the browser context
        // and reporting the terminal state to the Orchestrator (no auto-restart here).
        CrashClassified?.Invoke(this, new CrashClassifiedEventArgs(runId, terminalState, reason));
    }

    private async Task RecordCrashEntryAsync(string runId)
    {
        try
        {
            await journal.AppendStepAsync(
                    runId,
                    stepId:  "crash.detected",
                    action:  "browser.crashed",
                    outcome: "Unexpected browser process disconnection",
                    ct:      CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Logging only — we must not let a journal failure prevent crash classification
            logger.LogWarning(
                ex,
                "Failed to append crash entry to journal for run {RunId}. "
                + "Classification will proceed from available journal data.", runId);
        }
    }

    private async Task<RunState> ClassifyTerminalStateAsync(string runId)
    {
        try
        {
            var entries = await journal
                .ReadAsync(runId, CancellationToken.None)
                .ConfigureAwait(false);

            return CrashStateClassifier.Classify(entries);
        }
        catch (Exception ex)
        {
            // Safe default: if journal cannot be read, assume uncertain state
            logger.LogWarning(
                ex,
                "Failed to read journal for run {RunId} during crash classification. "
                + "Defaulting to UNKNOWN for safety.", runId);
            return RunState.UNKNOWN;
        }
    }

    // ── Private helper record ─────────────────────────────────────────────────

    private sealed class MonitoredRun(IBrowser browser, string runId)
    {
        public IBrowser Browser { get; } = browser;
        public string RunId    { get; } = runId;

        /// <summary>
        /// Set to <c>true</c> by <see cref="Detach"/> before a clean browser close, so that
        /// the subsequent <c>Disconnected</c> event is not treated as a crash.
        /// </summary>
        public volatile bool Suppress;
    }
}
