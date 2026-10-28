using Tracto.TerminalRuntime.Domain.Process;

namespace Tracto.TerminalRuntime.Application.Process;

/// <summary>
/// Command: cancel the process tree described by <paramref name="Tree"/>.
///
/// CEC TER-US-015 AC-3, AC-5, AC-6:
///   The cancel signal terminates the full owned child process tree.
///   After <see cref="ProcessTreeCleanupMs"/> milliseconds any surviving PIDs
///   are force-killed.  A verification pass then confirms no owned process remains.
///   The run state transitions to CANCELLED.
///   Unrelated host processes are unaffected.
/// </summary>
/// <param name="RunId">Run that owns the process.</param>
/// <param name="AttemptId">Attempt identifier for idempotent state writes.</param>
/// <param name="Tree">The process tree to cancel, as returned by StartAsync.</param>
/// <param name="ProcessTreeCleanupMs">
///   Grace period in milliseconds before force-killing surviving processes.
///   Defaults to 5 000 ms.
/// </param>
public sealed record CancelProcessCommand(
    string RunId,
    string AttemptId,
    OwnedProcessTree Tree,
    int ProcessTreeCleanupMs = 5_000);
