#nullable enable
using System.Text.Json;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Domain.Telemetry;

namespace Tracto.Orchestrator.Application.Workers;

/// <summary>Command: mark a worker offline and flag its RUNNING runs as needing attention. OC-01.</summary>
public sealed record MarkWorkerOfflineCommand(string WorkerId);

public sealed class MarkWorkerOfflineCommandHandler
{
    private readonly IWorkerRepository _workers;
    private readonly IRunRepository    _runs;
    private readonly IUnitOfWork       _unitOfWork;
    private readonly IClock            _clock;
    private readonly ITelemetryService _telemetry;

    // Sentinel RunId for worker-level telemetry events that have no run context.
    private static readonly Guid WorkerTelemetryRunId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public MarkWorkerOfflineCommandHandler(
        IWorkerRepository workers,
        IRunRepository    runs,
        IUnitOfWork       unitOfWork,
        IClock            clock,
        ITelemetryService telemetry)
    {
        _workers    = workers;
        _runs       = runs;
        _unitOfWork = unitOfWork;
        _clock      = clock;
        _telemetry  = telemetry;
    }

    public async Task<Result<bool, Error>> HandleAsync(
        MarkWorkerOfflineCommand cmd,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;

        await _unitOfWork.BeginAsync(cancellationToken);

        var worker = await _workers.GetByIdAsync(cmd.WorkerId, cancellationToken);
        if (worker is null)
            return Result<bool, Error>.Fail(
                Error.NotFound($"Worker '{cmd.WorkerId}' not found."));

        // Mark offline — does not touch LastHeartbeatAt (domain invariant).
        worker.MarkOffline(now);
        _workers.Update(worker);

        // Both RUNNING runs (actively executing) and LEASED runs (worker took the lease but
        // never called Start) owned by this worker need human attention.  A leased-but-not-
        // started run is just as orphaned as a running one when the worker disappears.
        var runningRuns = await _runs.FindByWorkerAndStateAsync(
            cmd.WorkerId, RunState.Running, cancellationToken);
        var leasedRuns = await _runs.FindByWorkerAndStateAsync(
            cmd.WorkerId, RunState.Leased, cancellationToken);

        foreach (var run in runningRuns.Concat(leasedRuns))
        {
            run.MarkUnknown("Worker marked offline", now);
            _runs.Update(run);
        }

        await _unitOfWork.CommitAsync(cancellationToken);

        // Telemetry: WORKER_OFFLINE
        await _telemetry.EmitAsync(new TelemetryEvent(
            id:        Guid.NewGuid(),
            runId:     WorkerTelemetryRunId,
            eventType: TelemetryEventType.WorkerOffline,
            occurredAt: now,
            workerId:  cmd.WorkerId,
            metadata:  JsonSerializer.Serialize(new
            {
                affectedRunCount = runningRuns.Count + leasedRuns.Count,
            })),
            cancellationToken);

        // Telemetry: RUN_UNKNOWN for each affected run
        foreach (var run in runningRuns.Concat(leasedRuns))
        {
            await _telemetry.EmitAsync(new TelemetryEvent(
                id:        Guid.NewGuid(),
                runId:     run.Id,
                eventType: TelemetryEventType.RunUnknown,
                occurredAt: now,
                workerId:  cmd.WorkerId,
                attemptId: run.AttemptId,
                metadata:  JsonSerializer.Serialize(new { unknownReason = "Worker marked offline" })),
                cancellationToken);
        }

        return Result<bool, Error>.Ok(true);
    }
}
