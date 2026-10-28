using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain;
using Tracto.TerminalRuntime.Domain.Process;
using Microsoft.Extensions.Logging;

namespace Tracto.TerminalRuntime.Application.Process;

/// <summary>
/// Application-layer handler that validates and dispatches a process-start request.
///
/// Responsibilities (TER-US-015):
/// <list type="number">
///   <item>Enforce background-process policy (AC-7): reject if ArgumentList contains
///         background-spawning patterns and the template does not set AllowsBackground.</item>
///   <item>Delegate process launch to <see cref="IProcessController.StartAsync"/>.</item>
///   <item>On success, record the run state transition to RUNNING via IRunJournal.</item>
/// </list>
///
/// No process-launching logic lives here (CEC invariant #5).
/// No plaintext secrets in logs or error messages (CEC invariant #4).
/// </summary>
public sealed class StartProcessHandler(
    IProcessController controller,
    IRunJournal journal,
    ILogger<StartProcessHandler> logger)
{
    public async Task<Result<OwnedProcessTree, Error>> HandleAsync(
        StartProcessCommand command,
        CancellationToken ct = default)
    {
        // AC-7: Background-process gate.
        // Check before any I/O so the rejection is fast and produces no side effects.
        if (!command.AllowsBackground)
        {
            string? marker = BackgroundProcessPolicy.FindFirstMarker(command.Spec.ArgumentList);
            if (marker is not null)
            {
                logger.LogWarning(
                    "Run {RunId} rejected: ArgumentList contains background-spawning pattern [{Marker}] " +
                    "and the command template does not set AllowsBackground.",
                    command.RunId, marker);

                return Error.PolicyDenied(
                    "Process.BackgroundNotAllowed",
                    $"The command contains a background-spawning pattern ('{marker}') that is not " +
                    "permitted by the approved command template. " +
                    "Set AllowsBackground=true in the command registry entry to allow this lifecycle.");
            }
        }

        // Delegate launch to Infrastructure — no process logic here.
        var startResult = await controller.StartAsync(command, ct);
        if (startResult.IsFailure)
        {
            logger.LogError("Run {RunId} attempt {AttemptId}: process start failed: {Code}",
                command.RunId, command.AttemptId, startResult.Error.Code);
            return startResult.Error;
        }

        // Record RUNNING state so the run journal reflects the live PID before output is read.
        var stateResult = await journal.RecordStateAsync(
            command.RunId,
            command.AttemptId,
            leaseId: string.Empty,   // lease is pre-established; caller supplies it if needed
            RunState.Running,
            ct);

        if (stateResult.IsFailure)
        {
            logger.LogError("Run {RunId}: could not persist RUNNING state: {Code}",
                command.RunId, stateResult.Error.Code);
            // Non-fatal: the process is already running; do not fail the start.
            // The Orchestrator will reconcile via heartbeat or lease expiry.
        }

        return startResult.Value;
    }
}
