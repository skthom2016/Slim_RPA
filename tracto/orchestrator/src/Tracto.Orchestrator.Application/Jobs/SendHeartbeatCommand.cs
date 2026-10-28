#nullable enable
using Microsoft.Extensions.Logging;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Leases;

namespace Tracto.Orchestrator.Application.Jobs;

// ── Command ───────────────────────────────────────────────────────────────────

/// <summary>
/// Worker heartbeat: keeps the lease alive and polls for cancellation requests (OC-05).
/// </summary>
/// <param name="RunId">The run whose lease the worker is heartbeating.</param>
/// <param name="AttemptId">AttemptId echoed from the AcceptLease response — fences stale workers.</param>
/// <param name="WorkerId">The stable identifier of the worker sending the heartbeat.</param>
/// <param name="SeqNo">Monotonically increasing sequence number; stale values are rejected.</param>
public sealed record SendHeartbeatCommand(
    Guid   RunId,
    Guid   AttemptId,
    string WorkerId,
    long   SeqNo);

// ── Response ──────────────────────────────────────────────────────────────────

/// <summary>
/// Result returned to the worker on a successful heartbeat.
/// </summary>
/// <param name="CancellationRequested">
/// True when the orchestrator has flagged the run for cancellation.
/// The worker should stop processing and call the completion endpoint with Cancelled state.
/// </param>
public sealed record HeartbeatResult(bool CancellationRequested);

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// Handles <see cref="SendHeartbeatCommand"/> — story OC-05.
///
/// Algorithm (all writes inside a single atomic transaction):
///   1. FindActiveLeaseForRunAsync (non-Expired). Null → fall back to FindByRunIdAsync:
///        - Null            → 404 NotFound.
///        - State=Expired   → 409 LEASE_EXPIRED.
///   2a. WorkerId mismatch → 409 WRONG_WORKER.
///   2b. AttemptId mismatch → 409 WRONG_ATTEMPT.
///   3. SeqNo ≤ LastSeqNo  → Result.Fail("STALE_SEQ_NO"). LastHeartbeatAt is NOT updated.
///   4. LeaseRecord must be in Accepted state; if still Offered → 409 WRONG_STATE.
///   5. Update LastHeartbeatAt=now, LastSeqNo=SeqNo via LeaseRecord.RecordHeartbeat. Commit.
///   6. Return {cancellationRequested: LeaseRecord.CancellationRequested}.
/// </summary>
public sealed class SendHeartbeatCommandHandler
{
    private readonly ILeaseRepository _leaseRepo;
    private readonly IUnitOfWork      _unitOfWork;
    private readonly IClock           _clock;
    private readonly ILogger<SendHeartbeatCommandHandler> _logger;

    public SendHeartbeatCommandHandler(
        ILeaseRepository leaseRepo,
        IUnitOfWork      unitOfWork,
        IClock           clock,
        ILogger<SendHeartbeatCommandHandler> logger)
    {
        _leaseRepo  = leaseRepo;
        _unitOfWork = unitOfWork;
        _clock      = clock;
        _logger     = logger;
    }

    public async Task<Result<HeartbeatResult, Error>> HandleAsync(
        SendHeartbeatCommand command,
        CancellationToken    cancellationToken = default)
    {
        // ── Input guard ────────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(command.WorkerId))
            return Result<HeartbeatResult, Error>.Fail(
                Error.Validation("WorkerId is required."));

        // ── Step 1: resolve the active (non-Expired) lease for this run ────────
        var lease = await _leaseRepo.FindActiveLeaseForRunAsync(
            command.RunId, cancellationToken);

        if (lease is null)
        {
            // Distinguish between "no lease ever" and "lease already expired".
            var anyLease = await _leaseRepo.FindByRunIdAsync(
                command.RunId, cancellationToken);

            if (anyLease is null)
                return Result<HeartbeatResult, Error>.Fail(
                    Error.NotFound($"No lease found for run '{command.RunId}'."));

            // Lease exists but was expired by LeaseExpiryMonitorService.
            _logger.LogWarning(
                "Heartbeat: lease for run {RunId} is Expired (state={State}). WorkerId={WorkerId}.",
                command.RunId, anyLease.State, command.WorkerId);

            return Result<HeartbeatResult, Error>.Fail(
                Error.General("LEASE_EXPIRED",
                    $"Lease for run '{command.RunId}' has expired."));
        }

        // ── Step 2a: WorkerId fencing — reject heartbeats from the wrong worker ──
        // Checked before AttemptId so a rogue worker cannot probe valid AttemptIds
        // by observing which error code is returned.
        if (lease.WorkerId != command.WorkerId)
        {
            _logger.LogWarning(
                "Heartbeat: WorkerId mismatch for run {RunId}. Expected={Expected} Got={Got}",
                command.RunId, lease.WorkerId, command.WorkerId);

            return Result<HeartbeatResult, Error>.Fail(
                Error.General("WRONG_WORKER",
                    $"WorkerId mismatch for run '{command.RunId}'. " +
                    $"Expected '{lease.WorkerId}', received '{command.WorkerId}'."));
        }

        // ── Step 2b: AttemptId fencing ─────────────────────────────────────────
        if (lease.AttemptId != command.AttemptId)
        {
            _logger.LogWarning(
                "Heartbeat: AttemptId mismatch for run {RunId}. Expected={Expected} Got={Got}",
                command.RunId, lease.AttemptId, command.AttemptId);

            return Result<HeartbeatResult, Error>.Fail(
                Error.General("WRONG_ATTEMPT",
                    $"AttemptId mismatch for run '{command.RunId}'. " +
                    $"Expected '{lease.AttemptId}', received '{command.AttemptId}'."));
        }

        // ── Step 3: SeqNo monotonicity check (before acquiring write lock) ─────
        // Spec invariant 3: "Duplicate identical message → idempotent 200."
        //   SeqNo == LastSeqNo → already processed; return success without re-writing (idempotent).
        //   SeqNo <  LastSeqNo → truly stale (going backwards); return STALE_SEQ_NO (409).
        if (command.SeqNo == lease.LastSeqNo)
        {
            _logger.LogDebug(
                "Heartbeat: exact duplicate SeqNo {SeqNo} for run {RunId} — returning idempotent 200.",
                command.SeqNo, command.RunId);

            // Return the current cancellation flag without touching any state.
            return Result<HeartbeatResult, Error>.Ok(
                new HeartbeatResult(lease.CancellationRequested));
        }

        if (command.SeqNo < lease.LastSeqNo)
        {
            _logger.LogWarning(
                "Heartbeat: stale SeqNo {SeqNo} (current={Current}) for run {RunId}.",
                command.SeqNo, lease.LastSeqNo, command.RunId);

            return Result<HeartbeatResult, Error>.Fail(
                Error.General("STALE_SEQ_NO",
                    $"Sequence number {command.SeqNo} is lower than the last accepted LastSeqNo {lease.LastSeqNo} " +
                    $"for run '{command.RunId}'."));
        }

        // ── Step 4: must be Accepted (not Offered) ─────────────────────────────
        if (lease.State == LeaseState.Offered)
        {
            _logger.LogWarning(
                "Heartbeat: lease for run {RunId} is still Offered (not Accepted). WorkerId={WorkerId}.",
                command.RunId, command.WorkerId);

            return Result<HeartbeatResult, Error>.Fail(
                Error.General("WRONG_STATE",
                    $"Lease for run '{command.RunId}' has not been accepted yet."));
        }

        // ── Step 5: record heartbeat atomically ────────────────────────────────
        await _unitOfWork.BeginAsync(cancellationToken);
        var committed = false;

        try
        {
            var now = _clock.UtcNow;

            lease.RecordHeartbeat(now, command.SeqNo);
            _leaseRepo.Update(lease);

            await _unitOfWork.CommitAsync(cancellationToken);
            committed = true;

            _logger.LogDebug(
                "Heartbeat recorded for run {RunId} (attempt={AttemptId}, seqNo={SeqNo}, cancellationRequested={Cancelled}).",
                command.RunId, command.AttemptId, command.SeqNo, lease.CancellationRequested);

            // ── Step 6: return cancellation flag ──────────────────────────────
            return Result<HeartbeatResult, Error>.Ok(
                new HeartbeatResult(lease.CancellationRequested));
        }
        finally
        {
            if (!committed)
                await _unitOfWork.RollbackAsync();
        }
    }
}
