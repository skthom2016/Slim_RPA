using Microsoft.Playwright;

namespace TractoRuntime.Browser.Crash;

/// <summary>
/// Monitors a live browser process for unexpected disconnections (crashes) during a run.
///
/// The monitor subscribes to Playwright's <c>IBrowser.Disconnected</c> event when
/// <see cref="Attach"/> is called, and fires <see cref="CrashClassified"/> if the browser
/// exits before <see cref="Detach"/> is called for that run.
///
/// Call <see cref="Detach"/> on clean run termination (success, cancellation, planned shutdown)
/// so that normal browser-close events are not misidentified as crashes.
/// </summary>
public interface IBrowserProcessMonitor : IDisposable
{
    /// <summary>
    /// Attaches crash monitoring to <paramref name="browser"/> for <paramref name="runId"/>.
    /// Must be called after the browser is launched and before the first action executes (AC#2).
    /// </summary>
    /// <param name="browser">The Playwright browser instance to monitor.</param>
    /// <param name="runId">Identifies which run owns this browser process.</param>
    void Attach(IBrowser browser, string runId);

    /// <summary>
    /// Detaches monitoring for <paramref name="runId"/>.
    /// Call this on any clean terminal state so the subsequent browser close event
    /// is not classified as a crash.
    /// </summary>
    /// <param name="runId">The run whose monitoring should be deactivated.</param>
    void Detach(string runId);

    /// <summary>
    /// Raised on the thread-pool when an unexpected browser disconnection is detected and
    /// the terminal run state has been classified from the run journal.
    /// The worker should handle this event to propagate the terminal state to the Orchestrator.
    /// </summary>
    event EventHandler<CrashClassifiedEventArgs>? CrashClassified;
}
