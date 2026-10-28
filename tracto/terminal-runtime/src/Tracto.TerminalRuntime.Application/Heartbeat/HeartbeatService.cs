using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tracto.TerminalRuntime.Domain;

namespace Tracto.TerminalRuntime.Application.Heartbeat;

/// <summary>
/// Hosted background service that drives outbound heartbeats for all currently-running jobs
/// and enforces lease-expiry and max-deadline termination (TER-US-018).
///
/// <list type="bullet">
/// <item>
///   <b>AC-1</b>: heartbeats are sent at <see cref="HeartbeatPolicy.IntervalMs"/> intervals
///   while the command executes.  Each delivery is a bounded, secret-free
///   <see cref="HeartbeatRecord"/> (CEC invariant #4).
/// </item>
/// <item>
///   <b>AC-2</b>: after an Orchestrator restart the journal entry persisted by
///   <see cref="IRunJournal.RecordHeartbeatAsync"/> allows the Orchestrator to
///   verify the run was alive; the <see cref="ReconcileRunHandler"/> prevents an
///   irreversible command from being re-launched (CEC invariant #3).
/// </item>
/// <item>
///   <b>AC-3</b>: heartbeat failures do NOT kill the running command.  The command
///   continues within the approved lease duration and run's maximum deadline.  Only when
///   <see cref="HeartbeatPolicy.MaxFailures"/> consecutive failures accumulate AND the
///   lease has expired (or <see cref="HeartbeatContext.MaxDeadline"/> is reached) does
///   the service terminate the run safely and update the journal:
///   <list type="bullet">
///     <item><see cref="RunState.TimedOut"/> — no irreversible side effect committed.</item>
///     <item><see cref="RunState.Unknown"/> — irreversible side effect was committed
///       (CEC invariant #2: always <c>needsAttention = true</c>).</item>
///   </list>
/// </item>
/// </list>
///
/// No process-launch or network logic lives here (CEC invariant #5 — only the
/// <see cref="IHeartbeatSender"/> abstraction is called).
/// </summary>
public sealed class HeartbeatService(
    IHeartbeatSender           sender,
    IRunJournal                journal,
    IClock                     clock,
    ILogger<HeartbeatService>  logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, ActiveSession> _sessions =
        new(StringComparer.Ordinal);

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers a heartbeat session for a newly-started run and immediately spawns
    /// the per-session loop.
    ///
    /// Idempotent: a duplicate registration for the same <see cref="HeartbeatContext.RunId"/>
    /// is silently ignored.
    /// </summary>
    /// <param name="context">Heartbeat parameters for this run.  Ownership passes to this service.</param>
    /// <param name="commandCancellation">
    ///   Token from the command runner; when it fires (normal completion or external cancel),
    ///   the heartbeat loop exits cleanly without recording a failure state.
    /// </param>
    public void StartSession(
        HeartbeatContext context,
        CancellationToken commandCancellation = default)
    {
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(commandCancellation);
        var session   = new ActiveSession(context, linkedCts);

        if (!_sessions.TryAdd(context.RunId, session))
        {
            logger.LogWarning(
                "Heartbeat session already registered for run {RunId}. Ignoring duplicate.",
                context.RunId);
            linkedCts.Dispose();
            return;
        }

        session.LoopTask = Task.Run(() => RunSessionLoopAsync(session, linkedCts.Token));
    }

    /// <summary>
    /// Terminates the heartbeat session for the given run (call on normal command completion).
    /// The session is removed, its task is cancelled, and resources are released.
    /// </summary>
    public void StopSession(string runId)
    {
        if (_sessions.TryRemove(runId, out var session))
        {
            session.LinkedCts.Cancel();
            DisposeSession(session); // disposes LinkedCts only; context is caller-owned
        }
    }

    // ── BackgroundService ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Individual sessions each run their own Task; this task simply keeps the
            // service alive until the host signals shutdown.
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* expected on host shutdown */ }
        finally
        {
            // Snapshot the active sessions before cancelling so we can wait for them.
            var snapshot = _sessions.Values.ToArray();

            foreach (var s in snapshot)
                s.LinkedCts.Cancel();

            var pendingTasks = snapshot
                .Select(s => s.LoopTask ?? Task.CompletedTask)
                .ToArray();

            if (pendingTasks.Length > 0)
                await Task.WhenAll(pendingTasks).ConfigureAwait(false);

            // Dispose any session whose loop did not remove itself (edge case).
            foreach (var s in snapshot)
            {
                if (_sessions.TryRemove(s.Context.RunId, out _))
                    DisposeSession(s);
            }
        }
    }

    // ── Session loop ──────────────────────────────────────────────────────────

    private async Task RunSessionLoopAsync(ActiveSession session, CancellationToken ct)
    {
        var ctx = session.Context;

        logger.LogInformation(
            "Heartbeat session started for run {RunId} attempt {AttemptId} " +
            "leaseExpiry={LeaseExpiry:u} maxDeadline={MaxDeadline:u}.",
            ctx.RunId, ctx.AttemptId, ctx.LeaseExpiresAt, ctx.MaxDeadline);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(ctx.Policy.IntervalMs, ct).ConfigureAwait(false);

                var now = clock.UtcNow;

                // Hard stop: run has exceeded the absolute max deadline.
                if (now >= ctx.MaxDeadline)
                {
                    logger.LogWarning(
                        "Run {RunId}: max-deadline {Deadline:u} reached. Terminating command safely.",
                        ctx.RunId, ctx.MaxDeadline);
                    await TerminateRunAsync(ctx, ct).ConfigureAwait(false);
                    return;
                }

                var seqNo  = ctx.NextSeqNo();
                var record = new HeartbeatRecord(
                    RunId:           ctx.RunId,
                    AttemptId:       ctx.AttemptId,
                    SeqNo:           seqNo,
                    SentAt:          now,
                    ProgressSummary: BuildProgressSummary(ctx, seqNo));

                var sendResult = await sender.SendAsync(record, ct).ConfigureAwait(false);

                if (sendResult.IsSuccess)
                {
                    ctx.ResetFailures();
                    // AC-2: persist heartbeat sequence number to the journal.
                    await journal.RecordHeartbeatAsync(
                        ctx.RunId, ctx.AttemptId, (int)seqNo, ct).ConfigureAwait(false);

                    logger.LogDebug(
                        "Heartbeat seq={SeqNo} delivered for run {RunId}.", seqNo, ctx.RunId);
                }
                else
                {
                    // AC-3: failure does NOT kill the command — accumulate and decide.
                    var failures = ctx.IncrementFailures();

                    logger.LogWarning(
                        "Heartbeat failure {Failures}/{MaxFailures} for run {RunId}: [{Code}] {Message}",
                        failures, ctx.Policy.MaxFailures, ctx.RunId,
                        sendResult.Error.Code, sendResult.Error.Message);

                    // Terminate only when failures have accumulated AND the lease has expired.
                    // The running command may continue until both conditions are true.
                    if (failures >= ctx.Policy.MaxFailures && clock.UtcNow >= ctx.LeaseExpiresAt)
                    {
                        logger.LogError(
                            "Run {RunId}: {Failures} consecutive heartbeat failures and lease expired {LeaseExpiry:u}. " +
                            "Terminating command safely.",
                            ctx.RunId, failures, ctx.LeaseExpiresAt);
                        await TerminateRunAsync(ctx, ct).ConfigureAwait(false);
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal cancellation path: command completed or service is shutting down.
            logger.LogInformation("Heartbeat session for run {RunId} cancelled normally.", ctx.RunId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error in heartbeat loop for run {RunId}.", ctx.RunId);
        }
        finally
        {
            // Self-removal: if the loop exits for any reason, clean up the session.
            if (_sessions.TryRemove(ctx.RunId, out var removed))
                DisposeSession(removed);
        }
    }

    // ── Terminal state ────────────────────────────────────────────────────────

    private async Task TerminateRunAsync(HeartbeatContext ctx, CancellationToken ct)
    {
        // CEC v1.0.1 invariant #2:
        //   Irreversible side effect committed → UNKNOWN (needsAttention = true).
        //   No irreversible side effect committed → TIMED_OUT.
        var finalState = ctx.HasCommittedIrreversibleSideEffect
            ? RunState.Unknown
            : RunState.TimedOut;

        logger.LogError(
            "Run {RunId}: recording terminal state {State} " +
            "(irreversibleSideEffect={Irreversible}).",
            ctx.RunId, finalState, ctx.HasCommittedIrreversibleSideEffect);

        // CEC v1.0.1 invariant #2: UNKNOWN must be persisted via RecordUnknownStateAsync
        // so that NeedsAttention=true and UnknownReason are written atomically.
        // RecordStateAsync never sets those fields regardless of the state passed in.
        if (finalState == RunState.Unknown)
        {
            await journal.RecordUnknownStateAsync(
                ctx.RunId, ctx.AttemptId, ctx.LeaseId,
                "Heartbeat lease/deadline expiry with committed irreversible side effect",
                ct).ConfigureAwait(false);
        }
        else
        {
            await journal.RecordStateAsync(
                ctx.RunId, ctx.AttemptId, ctx.LeaseId, finalState, ct).ConfigureAwait(false);
        }

        // Signal the command runner to stop cleanly.
        ctx.RequestTermination();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a bounded progress summary that contains no secret values (CEC invariant #4).
    /// Maximum length: 512 characters.
    /// </summary>
    private static string BuildProgressSummary(HeartbeatContext ctx, long seqNo)
    {
        const int maxLen = 512;
        var raw = $"runId={ctx.RunId} attemptId={ctx.AttemptId} seqNo={seqNo}";
        return raw.Length <= maxLen ? raw : raw[..maxLen];
    }

    private static void DisposeSession(ActiveSession session)
    {
        // The HeartbeatContext is NOT disposed here: the caller that created the context
        // owns its lifetime (same pattern as CancellationToken / IDisposable consumers).
        // This method only disposes the LinkedCts that the service itself minted.
        try { session.LinkedCts.Dispose(); }
        catch { /* best-effort */ }
    }

    // ── Nested private type ───────────────────────────────────────────────────

    private sealed class ActiveSession(HeartbeatContext context, CancellationTokenSource linkedCts)
    {
        public HeartbeatContext        Context   { get; } = context;
        public CancellationTokenSource LinkedCts { get; } = linkedCts;

        /// <summary>
        /// The per-session background task.  Set immediately after <see cref="ConcurrentDictionary{TKey,TValue}.TryAdd"/>
        /// returns; may be null in the brief window between the two operations.
        /// </summary>
        public Task? LoopTask { get; set; }
    }
}
