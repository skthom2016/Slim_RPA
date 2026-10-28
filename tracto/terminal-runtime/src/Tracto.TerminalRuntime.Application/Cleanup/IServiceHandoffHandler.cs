using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Cleanup;

namespace Tracto.TerminalRuntime.Application.Cleanup;

/// <summary>
/// Contract for handling the async-acknowledgement outcome that a <c>service.invoke</c>
/// call returns, transitioning the run to <see cref="Domain.RunState.HandedOff"/> and
/// releasing the Terminal worker.
///
/// CEC v1.0.1 AC-3:
/// <list type="bullet">
///   <item>HANDED_OFF is persisted to the journal before this method returns.</item>
///   <item>The Terminal worker is released (no blocking wait on the remote service).</item>
///   <item>Run B (the continuation run) is NOT created here — that happens only when an
///         eligible, authenticated, deduplicated callback arrives.</item>
/// </list>
/// </summary>
public interface IServiceHandoffHandler
{
    /// <summary>
    /// Transitions run <paramref name="runId"/> / <paramref name="attemptId"/> to
    /// <see cref="Domain.RunState.HandedOff"/> and records the result in the journal.
    /// </summary>
    Task<Result<HandoffResult, Error>> HandleHandoffAsync(
        string runId,
        string attemptId,
        string leaseId,
        string? continuationMappingRef,
        CancellationToken ct = default);
}
