namespace TractoRuntime.Browser.Cancellation;

/// <summary>
/// Coordinates per-run cancellation signaling, bounded resource cleanup, and forced
/// lease and context release for BRW-US-016 — Cancellation and Resource Release.
///
/// <para><b>AC#1 — In-flight download and navigation cleanup</b><br/>
/// Cancellation is signalled via the run's <see cref="CancellationTokenSource"/>, which
/// propagates to every in-flight operation (downloads via <c>FileDownloadHandler</c>,
/// navigations via <c>NavigationExecutor</c>, and action steps via <c>ActionExecutor</c>).
/// Partial file cleanup is performed by those handlers when their token fires.</para>
///
/// <para><b>AC#2 — Bounded browser context and process release</b><br/>
/// After signalling cancellation, <see cref="CancelRunAsync"/> waits up to
/// <see cref="RunCancellationOptions.CleanupTimeout"/> for the run executor to call
/// <see cref="SignalCleanupComplete"/>.  If the timeout expires, the coordinator forcibly
/// releases the browser context so no orphaned context or process remains.</para>
///
/// <para><b>AC#3 — Lease and profile lock release</b><br/>
/// When a lease token was supplied at registration, the lease is released via the lease
/// manager in the forced-cleanup path.  In the normal path the run executor releases the
/// lease before calling <see cref="SignalCleanupComplete"/>.</para>
///
/// <para><b>AC#4 — No orphaned contexts or exposed credentials</b><br/>
/// The forced release path closes the browser context, which also removes the profile
/// lock for the run.  Partial file content is removed by <c>FileDownloadHandler</c>
/// (BRW-US-013 AC#5) and session tokens are never written outside the vault-backed
/// <c>ISessionStore</c>.</para>
/// </summary>
public interface IRunCancellationCoordinator
{
    /// <summary>
    /// Registers a run for cancellation tracking.
    /// Must be called before the run starts executing steps.
    /// </summary>
    /// <param name="runId">Stable identifier for the run (CEC-004).</param>
    /// <param name="runCts">
    ///   The <see cref="CancellationTokenSource"/> whose token is threaded through every
    ///   in-flight operation of this run.  The coordinator owns the decision of when to
    ///   cancel it; the caller must not cancel it directly.
    /// </param>
    /// <param name="leaseToken">
    ///   The lease token acquired from the Orchestrator for this run.  When supplied, it is
    ///   used in the forced-cleanup path to release the lease via the lease manager (AC#3).
    ///   Optional — omit only when the run has not yet acquired a lease.
    /// </param>
    /// <exception cref="InvalidOperationException">
    ///   Thrown when <paramref name="runId"/> is already registered.
    /// </exception>
    void RegisterRun(string runId, CancellationTokenSource runCts, string? leaseToken = null);

    /// <summary>
    /// Signals cancellation for the named run and waits for bounded cleanup.
    /// </summary>
    /// <remarks>
    /// <list type="number">
    ///   <item>Cancels the run's <see cref="CancellationTokenSource"/> (AC#1).</item>
    ///   <item>Waits up to <see cref="RunCancellationOptions.CleanupTimeout"/> for
    ///         <see cref="SignalCleanupComplete"/> to be called by the run executor.</item>
    ///   <item>On timeout, forcibly closes the browser context and releases the lease (AC#2, AC#3).</item>
    ///   <item>Closes the run journal with <c>CANCELLED</c> state in the forced path.</item>
    /// </list>
    /// </remarks>
    /// <param name="runId">Run whose cancellation is requested.</param>
    /// <param name="ct">
    ///   Cancellation token for the <em>cancel request itself</em> (e.g. the Orchestrator
    ///   connection dropped).  This is distinct from the run token — cancelling it aborts
    ///   the wait but still leaves the run token cancelled.
    /// </param>
    /// <returns>A <see cref="RunCancellationResult"/> describing the outcome.</returns>
    Task<RunCancellationResult> CancelRunAsync(string runId, CancellationToken ct);

    /// <summary>
    /// Signals that the run executor has finished all cleanup (browser context released,
    /// lease released, journal closed).  Allows <see cref="CancelRunAsync"/> to return
    /// <see cref="RunCancellationResult.CleanupCompletedInTime"/> as <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Safe to call even when no <see cref="CancelRunAsync"/> is pending — the signal is
    /// simply ignored.  Also safe to call after the forced-cleanup path has already run.
    /// </remarks>
    /// <param name="runId">Run that has completed cleanup.</param>
    void SignalCleanupComplete(string runId);

    /// <summary>Returns <see langword="true"/> when a run with <paramref name="runId"/> is currently registered.</summary>
    bool IsRunActive(string runId);
}
