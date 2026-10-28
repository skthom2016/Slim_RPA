using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Process;

namespace Tracto.TerminalRuntime.Application.Process;

/// <summary>
/// Abstraction over OS process lifecycle management for terminal command execution.
///
/// CEC TER-US-015 contract:
/// <list type="bullet">
///   <item>
///     <see cref="StartAsync"/> launches the process using a typed ArgumentList
///     (no shell-string concatenation), records its PID in the run journal before
///     any output is read, and returns the <see cref="OwnedProcessTree"/>.
///   </item>
///   <item>
///     <see cref="AwaitAsync"/> blocks until the process terminates or
///     <see cref="OwnedProcessTree.TimeoutMs"/> elapses.  On timeout it terminates
///     the full owned process tree and returns <see cref="ProcessOutcome.TimedOut"/>.
///   </item>
///   <item>
///     <see cref="CancelAsync"/> forcibly terminates the full owned process tree,
///     waits up to <paramref name="processTreeCleanupMs"/> for all PIDs to exit,
///     force-kills any survivors, and returns <see cref="ProcessOutcome.Cancelled"/>.
///   </item>
/// </list>
///
/// No business logic or direct third-party API calls in implementations (CEC invariant #5).
/// No plaintext secrets in logs or evidence (CEC invariant #4).
/// </summary>
public interface IProcessController
{
    /// <summary>
    /// Launches the process described by <paramref name="command"/>,
    /// records the root PID in the run journal, and returns the owned process tree.
    /// The PID is persisted before any stdout/stderr output is read (AC-1).
    /// </summary>
    /// <param name="command">Validated start parameters including shell, ArgumentList, cwd, env.</param>
    /// <param name="ct">Cancellation token (for the launch itself; does not terminate the child).</param>
    Task<Result<OwnedProcessTree, Error>> StartAsync(
        StartProcessCommand command,
        CancellationToken ct = default);

    /// <summary>
    /// Waits for the process identified by <paramref name="command"/> to exit
    /// or for its declared timeout to elapse.
    /// On timeout: terminates the full process tree and returns
    /// <see cref="ProcessOutcome.DeadlineExceeded"/> (AC-2, AC-4).
    /// On clean exit: returns exit code and stream-end markers (AC-2).
    /// </summary>
    Task<Result<ProcessOutcome, Error>> AwaitAsync(
        AwaitProcessCommand command,
        CancellationToken ct = default);

    /// <summary>
    /// Sends a cancel signal to the owned process tree.
    /// After <paramref name="processTreeCleanupMs"/> milliseconds any surviving PIDs
    /// are force-killed, and a verification pass confirms that no owned process remains.
    /// Returns <see cref="ProcessOutcome.CancelledByRequest"/> on success (AC-3, AC-5, AC-6).
    /// </summary>
    Task<Result<ProcessOutcome, Error>> CancelAsync(
        CancelProcessCommand command,
        int processTreeCleanupMs = 5_000,
        CancellationToken ct = default);
}
