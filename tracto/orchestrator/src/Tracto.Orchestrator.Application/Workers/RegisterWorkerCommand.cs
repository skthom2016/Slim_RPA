#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Telemetry;
using Tracto.Orchestrator.Domain.Workers;

namespace Tracto.Orchestrator.Application.Workers;

/// <summary>Command: register (or re-register) a worker with its capabilities. OC-01.</summary>
public sealed record RegisterWorkerCommand(
    string WorkerId,
    string RuntimeType,
    string[] ActionNames,
    string? CapabilityDigest,
    int AbsenceThresholdSeconds);

public sealed record RegisterWorkerResult(string WorkerId);

public sealed class RegisterWorkerCommandHandler
{
    private readonly IWorkerRepository _workers;
    private readonly IUnitOfWork       _unitOfWork;
    private readonly IClock            _clock;
    private readonly ITelemetryService _telemetry;
    private readonly ILogger<RegisterWorkerCommandHandler> _logger;

    // Sentinel RunId used for worker-level telemetry events (no run context).
    private static readonly Guid WorkerTelemetryRunId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public RegisterWorkerCommandHandler(
        IWorkerRepository workers,
        IUnitOfWork unitOfWork,
        IClock clock,
        ITelemetryService telemetry,
        ILogger<RegisterWorkerCommandHandler> logger)
    {
        _workers    = workers;
        _unitOfWork = unitOfWork;
        _clock      = clock;
        _telemetry  = telemetry;
        _logger     = logger;
    }

    public async Task<Result<RegisterWorkerResult, Error>> HandleAsync(
        RegisterWorkerCommand cmd,
        CancellationToken cancellationToken = default)
    {
        // Domain rule: WorkerId is mandatory.
        if (string.IsNullOrWhiteSpace(cmd.WorkerId))
            return Result<RegisterWorkerResult, Error>.Fail(
                Error.Validation("workerId required"));

        // Domain rule: RuntimeType is mandatory.
        if (string.IsNullOrWhiteSpace(cmd.RuntimeType))
            return Result<RegisterWorkerResult, Error>.Fail(
                Error.Validation("runtimeType required"));

        // Guard against callers that pass a null array (cmd.ActionNames is non-nullable but
        // JSON deserialisation can produce null at runtime; the API layer uses ?? [] but the
        // handler must be self-contained).
        var capability = new WorkerCapability(
            cmd.ActionNames ?? [],
            cmd.RuntimeType,
            cmd.CapabilityDigest);

        // Validate digest when provided — mismatch is a warning, not a failure (OC-01 spec).
        if (cmd.CapabilityDigest is not null)
        {
            var expected = ComputeDigest(cmd.ActionNames, cmd.RuntimeType);
            if (!string.Equals(expected, cmd.CapabilityDigest, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "[RegisterWorker] CapabilityDigest mismatch for worker '{WorkerId}'. " +
                    "Expected={Expected}, Received={Received}. Registering anyway.",
                    cmd.WorkerId, expected, cmd.CapabilityDigest);
            }
        }

        var now = _clock.UtcNow;

        await _unitOfWork.BeginAsync(cancellationToken);

        var existing = await _workers.GetByIdAsync(cmd.WorkerId, cancellationToken);
        if (existing is not null)
        {
            // Re-registration: update mutable fields, do NOT touch active leases.
            existing.UpdateRegistration(cmd.AbsenceThresholdSeconds, capability, now);
            _workers.Update(existing);
        }
        else
        {
            // New registration.
            var worker = new WorkerRecord(
                cmd.WorkerId,
                cmd.RuntimeType,
                cmd.AbsenceThresholdSeconds,
                now,
                capability);

            await _workers.AddAsync(worker, cancellationToken);
        }

        await _unitOfWork.CommitAsync(cancellationToken);

        // Telemetry: WORKER_REGISTERED — use sentinel RunId for worker-level events.
        await _telemetry.EmitAsync(new TelemetryEvent(
            id:        Guid.NewGuid(),
            runId:     WorkerTelemetryRunId,
            eventType: TelemetryEventType.WorkerRegistered,
            occurredAt: now,
            workerId:  cmd.WorkerId,
            metadata:  JsonSerializer.Serialize(new
            {
                runtimeType    = cmd.RuntimeType,
                actionCount    = cmd.ActionNames?.Length ?? 0,
                isReregistration = existing is not null,
            })),
            cancellationToken);

        return Result<RegisterWorkerResult, Error>.Ok(new RegisterWorkerResult(cmd.WorkerId));
    }

    /// <summary>
    /// Computes the expected SHA-256 hex digest over the canonical JSON of
    /// {ActionNames, RuntimeType} for digest-validation purposes.
    /// </summary>
    public static string ComputeDigest(string[] actionNames, string runtimeType)
    {
        // Canonical form: sorted action names so ordering doesn't matter.
        var sorted = actionNames.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var json   = JsonSerializer.Serialize(new { ActionNames = sorted, RuntimeType = runtimeType });
        var bytes  = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
