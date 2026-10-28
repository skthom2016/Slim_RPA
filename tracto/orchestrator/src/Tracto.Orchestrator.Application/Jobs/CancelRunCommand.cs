#nullable enable
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Journal;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Domain.Telemetry;

namespace Tracto.Orchestrator.Application.Jobs;

// -- Command -------------------------------------------------------------------

/// <summary>
/// Cancels an automation run. Behaviour depends on the run current state:
///   QUEUED   -> immediately transitions to CANCELLED and writes RUN_CANCELLED journal entry.
///   LEASED   -> immediately transitions to CANCELLED; any active lease is expired so that
///              a subsequent AcceptLease returns 409 LEASE_EXPIRED.
///   RUNNING  -> sets LeaseRecord.CancellationRequested = true; worker discovers on next heartbeat.
///              The run remains RUNNING until the worker acknowledges.
///   CANCELLED -> idempotent 200 (no write).
///   Terminal (HANDED_OFF | SUCCEEDED | FAILED | TIMED_OUT | UNKNOWN) -> 409 TERMINAL_STATE.
/// </summary>
/// <param name="RunId">Primary key of the run to cancel.</param>
/// <param name="RequestedBy">Human-readable identifier of who requested the cancellation (null treated as missing).</param>
public sealed record CancelRunCommand(Guid RunId, string? RequestedBy);

// -- Handler -------------------------------------------------------------------

/// <summary>
/// Handles <see cref="CancelRunCommand"/> -- story OC-09.
///
/// All writes are atomic inside a single EF Core transaction (domain invariant 5).
/// AttemptId fencing is not re-validated here: the caller of CancelRun does not
/// need to know the AttemptId; the fence is needed only for worker-facing operations.
/// </summary>
public sealed class CancelRunCommandHandler
{
    private readonly IRunRepository     _runRepo;
    private readonly ILeaseRepository   _leaseRepo;
    private readonly IJournalRepository _journalRepo;
    private readonly IUnitOfWork        _unitOfWork;
    private readonly IClock             _clock;
    private readonly ITelemetryService  _telemetry;
    private readonly ILogger<CancelRunCommandHandler> _logger;

    public CancelRunCommandHandler(
        IRunRepository     runRepo,
        ILeaseRepository   leaseRepo,
        IJournalRepository journalRepo,
        IUnitOfWork        unitOfWork,
        IClock             clock,
        ITelemetryService  telemetry,
        ILogger<CancelRunCommandHandler> logger)
    {
        _runRepo     = runRepo;
        _leaseRepo   = leaseRepo;
        _journalRepo = journalRepo;
        _unitOfWork  = unitOfWork;
        _clock       = clock;
        _telemetry   = telemetry;
        _logger      = logger;
    }

    public async Task<Result<Guid, Error>> HandleAsync(
        CancelRunCommand  command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.RequestedBy))
            return Result<Guid, Error>.Fail(Error.Validation("RequestedBy is required."));

        var run = await _runRepo.GetByIdAsync(command.RunId, cancellationToken);
        if (run is null)
            return Result<Guid, Error>.Fail(
                Error.NotFound($"Run '{command.RunId}' not found."));

        if (run.State == RunState.Cancelled)
        {
            _logger.LogInformation(
                "CancelRun: run {RunId} already CANCELLED (idempotent).", command.RunId);
            return Result<Guid, Error>.Ok(command.RunId);
        }

        if (run.IsTerminal())
        {
            _logger.LogWarning(
                "CancelRun: run {RunId} is in terminal state {State}; cannot cancel.",
                command.RunId, run.State);
            return Result<Guid, Error>.Fail(
                Error.General("TERMINAL_STATE",
                    $"Run '{command.RunId}' is in terminal state {run.State} and cannot be cancelled."));
        }

        await _unitOfWork.BeginAsync(cancellationToken);
        var committed = false;
        try
        {
            var now = _clock.UtcNow;

            if (run.State == RunState.Running)
            {
                var lease = await _leaseRepo.FindActiveLeaseForRunAsync(
                    command.RunId, cancellationToken);

                if (lease is not null)
                {
                    lease.RequestCancellation();
                    _leaseRepo.Update(lease);
                }
                else
                {
                    _logger.LogWarning(
                        "CancelRun: run {RunId} is RUNNING but has no active (non-Expired) lease.",
                        command.RunId);
                }

                await _journalRepo.AppendAsync(
                    new JournalEntry(
                        runId:      run.Id,
                        attemptId:  run.AttemptId,
                        eventType:  "RUN_CANCEL_REQUESTED",
                        occurredAt: now,
                        payload:    BuildPayload(command.RequestedBy)),
                    cancellationToken);

                _logger.LogInformation(
                    "CancelRun: cancellation requested for RUNNING run {RunId}.", command.RunId);
            }
            else
            {
                var priorState = run.State;

                var activeLease = await _leaseRepo.FindActiveLeaseForRunAsync(
                    command.RunId, cancellationToken);

                if (activeLease is not null)
                {
                    // ForceExpire handles both Offered and Accepted leases without throwing.
                    // A LEASED run can have an Accepted lease if AcceptLeaseCommandHandler
                    // crashed after lease.Accept() but before run.Start() — using Expire()
                    // alone would throw InvalidOperationException in that scenario.
                    activeLease.ForceExpire();
                    _leaseRepo.Update(activeLease);
                }

                run.Cancel(now);
                _runRepo.Update(run);

                await _journalRepo.AppendAsync(
                    new JournalEntry(
                        runId:      run.Id,
                        attemptId:  run.AttemptId,
                        eventType:  "RUN_CANCELLED",
                        occurredAt: now,
                        payload:    BuildPayload(command.RequestedBy)),
                    cancellationToken);

                Activity.Current?.SetTag("run.id", run.Id.ToString());
                Activity.Current?.SetTag("attempt.id", run.AttemptId.ToString());
                await _telemetry.EmitAsync(new TelemetryEvent(
                    id:        Guid.NewGuid(),
                    runId:     run.Id,
                    eventType: TelemetryEventType.RunCancelled,
                    occurredAt: now,
                    attemptId: run.AttemptId,
                    metadata:  JsonSerializer.Serialize(new { requestedBy = command.RequestedBy })),
                    cancellationToken);

                _logger.LogInformation(
                    "CancelRun: run {RunId} transitioned {PriorState}->CANCELLED.", command.RunId, priorState);
            }

            await _unitOfWork.CommitAsync(cancellationToken);
            committed = true;

            return Result<Guid, Error>.Ok(command.RunId);
        }
        finally
        {
            if (!committed)
                await _unitOfWork.RollbackAsync();
        }
    }

    /// <summary>
    /// Builds the JSON payload for cancellation journal entries using
    /// <see cref="System.Text.Json.JsonSerializer"/> so that all special characters
    /// (control chars, surrogates, quotes, backslashes, etc.) are correctly escaped.
    /// Manual string concatenation would produce invalid JSON for inputs containing
    /// characters like <c>\n</c>, <c>\r</c>, <c>\t</c>, or null bytes.
    /// </summary>
    private static string BuildPayload(string? requestedBy)
        => JsonSerializer.Serialize(new { requestedBy });
}
