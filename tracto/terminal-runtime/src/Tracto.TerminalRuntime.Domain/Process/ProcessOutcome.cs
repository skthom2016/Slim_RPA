namespace Tracto.TerminalRuntime.Domain.Process;

/// <summary>
/// Outcome of a process execution as returned by the process controller.
///
/// Exactly one of the three outcome signals will be true (or ExitCode will be
/// set on clean exit).  The controller guarantees that TimedOut and Cancelled
/// are mutually exclusive.
/// </summary>
/// <param name="ExitCode">
///   OS exit code when the process terminated normally (not by deadline or cancel).
///   Null when the process was killed by a timeout or cancellation signal.
/// </param>
/// <param name="TimedOut">
///   True when the declared <c>TimeoutMs</c> elapsed before the process exited.
///   Run state must transition to <see cref="RunState.TimedOut"/> in this case.
/// </param>
/// <param name="Cancelled">
///   True when the caller explicitly cancelled the process via CancelAsync.
///   Run state must transition to <see cref="RunState.Cancelled"/> in this case.
/// </param>
/// <param name="StdoutContent">
///   Full captured standard-output text, or <see langword="null"/> when output was not
///   redirected (e.g. on timeout/cancel before the pipe was fully drained).
///   AC-2: stream content returned on normal end.
/// </param>
/// <param name="StderrContent">
///   Full captured standard-error text, or <see langword="null"/> when not available.
///   AC-2: stream content returned on normal end.
/// </param>
/// <param name="StdoutComplete">
///   True when stdout reached EOF cleanly (i.e. <see cref="StdoutContent"/> holds the
///   complete output and the stream-end marker was observed).  AC-2.
/// </param>
/// <param name="StderrComplete">
///   True when stderr reached EOF cleanly.  AC-2.
/// </param>
public sealed record ProcessOutcome(
    int? ExitCode,
    bool TimedOut,
    bool Cancelled,
    string? StdoutContent = null,
    string? StderrContent = null,
    bool StdoutComplete = false,
    bool StderrComplete = false)
{
    /// <summary>True when the process exited cleanly (not timed out, not cancelled).</summary>
    public bool IsCleanExit => !TimedOut && !Cancelled;

    /// <summary>True when the exit code is zero and the process exited cleanly.</summary>
    public bool IsSuccess => IsCleanExit && ExitCode == 0;

    /// <summary>
    /// Clean exit with the supplied exit code and optional captured stream content.
    /// <paramref name="stdoutContent"/> and <paramref name="stderrContent"/> are considered
    /// complete (EOF observed) whenever they are non-null.  AC-2.
    /// </summary>
    public static ProcessOutcome Succeeded(
        int exitCode = 0,
        string? stdoutContent = null,
        string? stderrContent = null) =>
        new(
            ExitCode:        exitCode,
            TimedOut:        false,
            Cancelled:       false,
            StdoutContent:   stdoutContent,
            StderrContent:   stderrContent,
            StdoutComplete:  stdoutContent is not null,
            StderrComplete:  stderrContent is not null);

    /// <summary>Process was forcibly terminated by the deadline.</summary>
    public static readonly ProcessOutcome DeadlineExceeded = new(null, true, false);

    /// <summary>Process was explicitly cancelled by the caller.</summary>
    public static readonly ProcessOutcome CancelledByRequest = new(null, false, true);
}
