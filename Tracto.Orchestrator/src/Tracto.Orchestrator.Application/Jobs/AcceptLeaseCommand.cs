#nullable enable
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Journal;
using Tracto.Orchestrator.Domain.Leases;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Domain.Telemetry;

namespace Tracto.Orchestrator.Application.Jobs;

// ── Command ───────────────────────────────────────────────────────────────────

/// <summary>
/// Worker's request to accept a lease offer and transition the run to RUNNING.
/// Implements domain invariant 3 (AttemptId fencing) and invariant 2 (UNKNOWN state rules).
/// </summary>
/// <param name="RunId">The run the worker is accepting the lease for.</param>
/// <param name="AttemptId">The attemptId echoed back from the PullJob response.</param>
/// <param name="WorkerId">The stable worker identifier that received the offer.</param>
public sealed record AcceptLeaseCommand(
    Guid   RunId,
    Guid   AttemptId,
    string WorkerId);

// Note: LeasePayload is the Domain value-object (Domain.Leases.LeasePayload).
// It is used directly as the handler return type — no Application-layer duplicate.
// Domain.Leases.LeasePayload.Create(...) enforces invariant 4 (no plaintext secrets)
// at construction time via AssertNoResolvedSecrets().

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// Handles <see cref="AcceptLeaseCommand"/> inside a serializable DB transaction.
///
/// Algorithm (all steps inside a single atomic transaction):
///   1. Load LeaseRecord for RunId via FindActiveLeaseForRunAsync (non-Expired leases).
///      If null, fall back to FindByRunIdAsync to distinguish:
///        - No lease ever issued → 404 NotFound.
///        - Lease exists but background job already expired it → 409 LEASE_EXPIRED
///          (after WorkerId + AttemptId checks per spec steps 2 and 3).
///   2. WorkerId mismatch → 409 WRONG_WORKER.
///   3. AttemptId mismatch → 409 WRONG_ATTEMPT.
///   4. Lease expired (ExpiresAt &lt; now):
///      - If journal has RUN_RUNNING entries → requeue run (State=QUEUED) via Requeue().
///      - Else → run State=UNKNOWN, NeedsAttention=true, UnknownReason set.
///      - Return 409 LEASE_EXPIRED (either path).
///   5. Idempotent: same WorkerId + AttemptId, lease already Accepted → 200 (no second attempt).
///   6. Success: LeaseRecord.State=Accepted, AcceptedAt=now.
///      RunRecord State=LEASED→RUNNING. JournalEntry("RUN_RUNNING"). Commit.
///      Return LeasePayload with no resolved secret values.
/// </summary>
public sealed class AcceptLeaseCommandHandler
{
    private readonly IRunRepository     _runRepo;
    private readonly ILeaseRepository   _leaseRepo;
    private readonly IJournalRepository _journalRepo;
    private readonly IUnitOfWork        _unitOfWork;
    private readonly IClock             _clock;
    private readonly ITelemetryService  _telemetry;
    private readonly ILogger<AcceptLeaseCommandHandler> _logger;

    public AcceptLeaseCommandHandler(
        IRunRepository     runRepo,
        ILeaseRepository   leaseRepo,
        IJournalRepository journalRepo,
        IUnitOfWork        unitOfWork,
        IClock             clock,
        ITelemetryService  telemetry,
        ILogger<AcceptLeaseCommandHandler> logger)
    {
        _runRepo     = runRepo;
        _leaseRepo   = leaseRepo;
        _journalRepo = journalRepo;
        _unitOfWork  = unitOfWork;
        _clock       = clock;
        _telemetry   = telemetry;
        _logger      = logger;
    }

    public async Task<Result<LeasePayload, Error>> HandleAsync(
        AcceptLeaseCommand command,
        CancellationToken  cancellationToken = default)
    {
        // ── Input guard ────────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(command.WorkerId))
            return Result<LeasePayload, Error>.Fail(
                Error.Validation("WorkerId is required."));

        await _unitOfWork.BeginAsync(cancellationToken);

        var committed = false;
        try
        {
            var now = _clock.UtcNow;

            // ── Step 1: load the active (non-Expired) lease for this run ───────
            // FindActiveLeaseForRunAsync filters State != Expired.
            // When it returns null we must distinguish two cases:
            //   a) No lease was ever issued → 404 NotFound.
            //   b) A lease was issued but the background expiry job already marked it
            //      State=Expired before this AcceptLease arrived → 409 LEASE_EXPIRED.
            var lease = await _leaseRepo.FindActiveLeaseForRunAsync(
                command.RunId, cancellationToken);

            if (lease is null)
            {
                // Fall back to any-state lookup to detect background-expired leases.
                var expiredLease = await _leaseRepo.FindByRunIdAsync(
                    command.RunId, cancellationToken);

                if (expiredLease is null)
                    return Result<LeasePayload, Error>.Fail(
                        Error.NotFound($"No active lease found for run '{command.RunId}'."));

                // A lease was issued but the background job explicitly expired it.
                // Apply WorkerId + AttemptId checks (spec steps 2 & 3) before surfacing
                // LEASE_EXPIRED so the caller receives a precise error code.
                if (!string.Equals(expiredLease.WorkerId, command.WorkerId, StringComparison.Ordinal))
                {
                    _logger.LogWarning(
                        "AcceptLease (pre-expired): WorkerId mismatch for run {RunId}. Expected={Expected} Got={Got}",
                        command.RunId, expiredLease.WorkerId, command.WorkerId);
                    return Result<LeasePayload, Error>.Fail(
                        Error.General("WRONG_WORKER",
                            $"Lease for run '{command.RunId}' belongs to worker '{expiredLease.WorkerId}', not '{command.WorkerId}'."));
                }

                if (expiredLease.AttemptId != command.AttemptId)
                {
                    _logger.LogWarning(
                        "AcceptLease (pre-expired): AttemptId mismatch for run {RunId}. Expected={Expected} Got={Got}",
                        command.RunId, expiredLease.AttemptId, command.AttemptId);
                    return Result<LeasePayload, Error>.Fail(
                        Error.General("WRONG_ATTEMPT",
                            $"AttemptId mismatch for run '{command.RunId}'. " +
                            $"Expected '{expiredLease.AttemptId}', received '{command.AttemptId}'."));
                }

                _logger.LogWarning(
                    "AcceptLease: background job already expired lease for run {RunId} before worker arrived.",
                    command.RunId);
                return Result<LeasePayload, Error>.Fail(
                    Error.General("LEASE_EXPIRED",
                        $"Lease for run '{command.RunId}' expired at {expiredLease.ExpiresAt:O}."));
            }

            // ── Step 2: WorkerId must match ────────────────────────────────────
            if (!string.Equals(lease.WorkerId, command.WorkerId, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "AcceptLease: WorkerId mismatch for run {RunId}. Expected={Expected} Got={Got}",
                    command.RunId, lease.WorkerId, command.WorkerId);
                return Result<LeasePayload, Error>.Fail(
                    Error.General("WRONG_WORKER",
                        $"Lease for run '{command.RunId}' belongs to worker '{lease.WorkerId}', not '{command.WorkerId}'."));
            }

            // ── Step 3: AttemptId must match ───────────────────────────────────
            if (lease.AttemptId != command.AttemptId)
            {
                _logger.LogWarning(
                    "AcceptLease: AttemptId mismatch for run {RunId}. Expected={Expected} Got={Got}",
                    command.RunId, lease.AttemptId, command.AttemptId);
                return Result<LeasePayload, Error>.Fail(
                    Error.General("WRONG_ATTEMPT",
                        $"AttemptId mismatch for run '{command.RunId}'. " +
                        $"Expected '{lease.AttemptId}', received '{command.AttemptId}'."));
            }

            // ── Step 5 (before Step 4): idempotent path — already Accepted ────
            // If same WorkerId + same AttemptId and lease is already Accepted,
            // return 200 with the same payload (no second state transition).
            if (lease.State == LeaseState.Accepted)
            {
                var run = await _runRepo.GetByIdAsync(command.RunId, cancellationToken);
                if (run is null)
                    return Result<LeasePayload, Error>.Fail(
                        Error.NotFound($"Run '{command.RunId}' not found."));

                _logger.LogInformation(
                    "AcceptLease idempotent: run {RunId} already RUNNING (lease already Accepted).",
                    command.RunId);

                // No commit needed — nothing changed.
                return Result<LeasePayload, Error>.Ok(BuildPayload(run, lease));
            }

            // ── Step 4: Lease expired ──────────────────────────────────────────
            if (lease.ExpiresAt < now)
            {
                var run = await _runRepo.GetByIdAsync(command.RunId, cancellationToken);
                if (run is null)
                    return Result<LeasePayload, Error>.Fail(
                        Error.NotFound($"Run '{command.RunId}' not found."));

                // Determine whether start was already definitively confirmed.
                var hasRunningEntries = await _journalRepo.HasEntriesOfTypeAsync(
                    command.RunId, "RUN_RUNNING", cancellationToken);

                if (hasRunningEntries)
                {
                    // Start was confirmed in a previous attempt — safe to re-queue.
                    // The run is LEASED (PullJob already transitioned it before the lease expired).
                    // Requeue requires LEASED state and issues a fresh AttemptId to fence stale
                    // in-flight messages (domain invariant 3).
                    run.Requeue(now);
                    _runRepo.Update(run);

                    _logger.LogWarning(
                        "AcceptLease: lease expired for run {RunId} but RUN_RUNNING confirmed — requeueing.",
                        command.RunId);

                    Activity.Current?.SetTag("run.id", run.Id.ToString());
                    Activity.Current?.SetTag("attempt.id", run.AttemptId.ToString());
                    await _telemetry.EmitAsync(new TelemetryEvent(
                        id:        Guid.NewGuid(),
                        runId:     run.Id,
                        eventType: TelemetryEventType.RunQueued,
                        occurredAt: now,
                        workerId:  command.WorkerId,
                        attemptId: run.AttemptId,
                        metadata:  JsonSerializer.Serialize(new { reason = "lease_expired_requeued" })),
                        cancellationToken);
                }
                else
                {
                    // Start is uncertain — mark UNKNOWN, never auto-replay.
                    run.MarkUnknown("Late accept; start uncertain", now);
                    _runRepo.Update(run);

                    _logger.LogWarning(
                        "AcceptLease: lease expired for run {RunId} with no RUN_RUNNING evidence — marked UNKNOWN.",
                        command.RunId);

                    Activity.Current?.SetTag("run.id", run.Id.ToString());
                    Activity.Current?.SetTag("attempt.id", command.AttemptId.ToString());
                    await _telemetry.EmitAsync(new TelemetryEvent(
                        id:        Guid.NewGuid(),
                        runId:     run.Id,
                        eventType: TelemetryEventType.RunUnknown,
                        occurredAt: now,
                        workerId:  command.WorkerId,
                        attemptId: command.AttemptId,
                        metadata:  JsonSerializer.Serialize(new { unknownReason = "Late accept; start uncertain" })),
                        cancellationToken);
                }

                lease.Expire();
                _leaseRepo.Update(lease);

                await _unitOfWork.CommitAsync(cancellationToken);
                committed = true;

                return Result<LeasePayload, Error>.Fail(
                    Error.General("LEASE_EXPIRED",
                        $"Lease for run '{command.RunId}' expired at {lease.ExpiresAt:O}."));
            }

            // ── Step 6: Happy path — accept the lease ─────────────────────────
            {
                var run = await _runRepo.GetByIdAsync(command.RunId, cancellationToken);
                if (run is null)
                    return Result<LeasePayload, Error>.Fail(
                        Error.NotFound($"Run '{command.RunId}' not found."));

                // Transition: LeaseRecord Offered→Accepted.
                lease.Accept(now);
                _leaseRepo.Update(lease);

                // RunRecord is QUEUED in production (PullJob leaves it there).
                // AdoptLease adopts the lease AttemptId as the new run-level fence and
                // transitions QUEUED→LEASED. In recovery scenarios where the run is already
                // LEASED (e.g. crash between Lease and Start), we skip AdoptLease.
                if (run.State == RunState.Queued)
                    run.AdoptLease(command.AttemptId, command.WorkerId, now);

                // LEASED → RUNNING (command.AttemptId was adopted above, so EnsureAttemptId passes).
                run.Start(command.AttemptId, now);
                _runRepo.Update(run);

                // Append atomic journal entry (domain invariant 5).
                await _journalRepo.AppendAsync(
                    new JournalEntry(
                        runId:      run.Id,
                        attemptId:  command.AttemptId,
                        eventType:  "RUN_RUNNING",
                        occurredAt: now,
                        payload:    $"{{\"workerId\":\"{command.WorkerId}\"}}"),
                    cancellationToken);

                // Telemetry: RUN_LEASED + RUN_RUNNING
                Activity.Current?.SetTag("run.id", run.Id.ToString());
                Activity.Current?.SetTag("attempt.id", command.AttemptId.ToString());
                await _telemetry.EmitAsync(new TelemetryEvent(
                    id:        Guid.NewGuid(),
                    runId:     run.Id,
                    eventType: TelemetryEventType.RunLeased,
                    occurredAt: now,
                    workerId:  command.WorkerId,
                    leaseId:   lease.LeaseId,
                    attemptId: command.AttemptId),
                    cancellationToken);
                await _telemetry.EmitAsync(new TelemetryEvent(
                    id:        Guid.NewGuid(),
                    runId:     run.Id,
                    eventType: TelemetryEventType.RunRunning,
                    occurredAt: now,
                    workerId:  command.WorkerId,
                    leaseId:   lease.LeaseId,
                    attemptId: command.AttemptId),
                    cancellationToken);

                await _unitOfWork.CommitAsync(cancellationToken);
                committed = true;

                _logger.LogInformation(
                    "AcceptLease: run {RunId} transitioned to RUNNING (worker={WorkerId}, attempt={AttemptId}).",
                    run.Id, command.WorkerId, command.AttemptId);

                // Build payload — domain LeasePayload.Create enforces invariant 4.
                return Result<LeasePayload, Error>.Ok(BuildPayload(run, lease));
            }
        }
        finally
        {
            if (!committed)
                await _unitOfWork.RollbackAsync();
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the lease payload returned to the worker using the Domain value-object factory.
    /// <see cref="LeasePayload.Create"/> calls <c>AssertNoResolvedSecrets()</c> immediately,
    /// enforcing domain invariant 4: every SecretRef must begin with <c>"ref:"</c>.
    /// Throws <see cref="InvalidOperationException"/> if the invariant is violated.
    /// SecretRefs are opaque references stored on the RunRecord — never resolved values.
    /// </summary>
    private static LeasePayload BuildPayload(RunRecord run, LeaseRecord lease)
        => LeasePayload.Create(
            runId:           run.Id,
            attemptId:       lease.AttemptId,
            artifactId:      lease.ArtifactId,
            artifactVersion: lease.ArtifactVersion,
            artifactHash:    lease.ArtifactHash,
            inputRefs:       run.InputRefs,
            secretRefs:      run.SecretRefs);   // opaque refs only — Create() throws on plaintext
}
