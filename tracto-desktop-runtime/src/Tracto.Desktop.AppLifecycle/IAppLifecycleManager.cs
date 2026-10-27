namespace Tracto.Desktop.AppLifecycle;

/// <summary>
/// Manages the lifecycle of Windows application processes: launch, attach, close and crash detection.
/// Retry class for launch and attach is NEVER — uncertain operations yield UNKNOWN, never a blind retry.
/// </summary>
public interface IAppLifecycleManager
{
    /// <summary>
    /// Launches the executable described by <paramref name="options"/>, waits for the process to become
    /// idle, verifies that it is still alive and returns its <see cref="AppHandle"/>.
    /// </summary>
    /// <param name="options">Launch parameters. The executable must be on the policy allowlist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="AppHandle"/> describing the running process.</returns>
    /// <exception cref="DesktopActionError">
    /// Thrown with appropriate <see cref="DesktopActionError.ErrorCategory"/> when the launch fails.
    /// </exception>
    Task<AppHandle> LaunchAsync(AppLaunchOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches to a running process that matches <paramref name="options"/>.
    /// </summary>
    /// <param name="options">Attach discriminators. At least one field must be set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="AppHandle"/> for the matched process.</returns>
    /// <exception cref="DesktopActionError">
    /// Thrown with <c>TARGET_NOT_FOUND</c> when no process matches, or <c>TARGET_AMBIGUOUS</c> when
    /// more than one process matches without a unique discriminator.
    /// </exception>
    Task<AppHandle> AttachAsync(AppAttachOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a close signal to the process identified by <paramref name="handle"/> and waits up to
    /// <paramref name="gracefulTimeoutMs"/> milliseconds for it to exit.
    /// </summary>
    /// <param name="handle">Handle returned by <see cref="LaunchAsync"/> or <see cref="AttachAsync"/>.</param>
    /// <param name="gracefulTimeoutMs">Milliseconds to wait for a clean exit before giving up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when the process exited within the timeout; <c>false</c> otherwise.</returns>
    Task<bool> CloseAsync(AppHandle handle, int gracefulTimeoutMs, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to unexpected process exit for the process described by <paramref name="handle"/>.
    /// When the process terminates, <paramref name="onExit"/> is invoked with a
    /// <see cref="DesktopActionError"/> whose category is <c>ENVIRONMENT</c> and detail code is
    /// <c>desktop.appCrash</c>.
    /// </summary>
    /// <param name="handle">Handle of the process to monitor.</param>
    /// <param name="onExit">Callback invoked on unexpected exit. Must not throw.</param>
    void MonitorForExit(AppHandle handle, Action<DesktopActionError> onExit);
}
