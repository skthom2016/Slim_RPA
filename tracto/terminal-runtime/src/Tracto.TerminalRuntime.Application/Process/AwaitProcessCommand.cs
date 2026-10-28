using Tracto.TerminalRuntime.Domain.Process;

namespace Tracto.TerminalRuntime.Application.Process;

/// <summary>
/// Command: block until the process described by <paramref name="Tree"/> exits
/// or its declared timeout elapses.
///
/// CEC TER-US-015 AC-2:
///   On normal exit, exit code and stream-end markers are returned.
///   On timeout, the full owned process tree is terminated and the run state
///   transitions to TIMED_OUT (AC-4).  The runtime never waits indefinitely.
/// </summary>
/// <param name="RunId">Run that owns the process.</param>
/// <param name="AttemptId">Attempt identifier for idempotent state writes.</param>
/// <param name="Tree">The process tree to await, as returned by StartAsync.</param>
public sealed record AwaitProcessCommand(
    string RunId,
    string AttemptId,
    OwnedProcessTree Tree);
