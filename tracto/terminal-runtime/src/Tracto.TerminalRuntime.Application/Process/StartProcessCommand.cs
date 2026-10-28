using Tracto.TerminalRuntime.Domain.Arguments;

namespace Tracto.TerminalRuntime.Application.Process;

/// <summary>
/// Command: start a new OS process using the given validated start specification.
///
/// CEC TER-US-015 AC-1:
///   The process must be launched via the typed <see cref="ProcessStartSpec.ArgumentList"/>
///   (no shell-string concatenation of user-supplied values).
///   The root PID must be recorded in the run journal before any output is read.
/// </summary>
/// <param name="RunId">Identifier of the logical run that owns this process.</param>
/// <param name="AttemptId">Current attempt identifier (fences idempotency, CEC invariant #3).</param>
/// <param name="Spec">Fully-validated process start specification.</param>
/// <param name="TimeoutMs">
///   Maximum allowed execution time in milliseconds.
///   Zero or negative means no deadline (use with care).
/// </param>
/// <param name="AllowsBackground">
///   Whether the approved command template permits background-spawning patterns.
///   Derived from <c>CommandRegistryEntry.AllowsBackground</c> (TER-US-015 AC-7).
/// </param>
public sealed record StartProcessCommand(
    string RunId,
    string AttemptId,
    ProcessStartSpec Spec,
    int TimeoutMs,
    bool AllowsBackground);
