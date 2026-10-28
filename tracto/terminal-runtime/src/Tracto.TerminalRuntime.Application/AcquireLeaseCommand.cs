using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain;

namespace Tracto.TerminalRuntime.Application;

// ---------------------------------------------------------------------------
// Command
// ---------------------------------------------------------------------------

/// <summary>
/// Requests a new job lease for <see cref="WorkerId"/>.
///
/// The handler enforces the one-lease-per-worker invariant (TER-US-022, AC-1):
/// if the worker already holds an active lease the command is rejected with
/// <c>Error.General("Lease.AlreadyHeld", …)</c>.
/// </summary>
public sealed record AcquireLeaseCommand(
    string RunId,
    string AttemptId,
    string WorkerId,
    IdempotencyClass IdempotencyClass);

// ---------------------------------------------------------------------------
// Result
// ---------------------------------------------------------------------------

/// <summary>Returned on successful lease acquisition.</summary>
public sealed record LeaseAcquired(
    string RunId,
    string AttemptId,
    string LeaseId,
    DateTimeOffset AcquiredAt);

// ---------------------------------------------------------------------------
// Handler
// ---------------------------------------------------------------------------

/// <summary>
/// Implements the lease-acquisition flow described in TER-US-022.
///
/// <list type="number">
///   <item>Check that the worker does not already hold an active lease.</item>
///   <item>Mint a new <c>LeaseId</c> and write the initial journal entry.</item>
///   <item>Return the minted <c>LeaseId</c> to the caller.</item>
/// </list>
///
/// No process-launch or network logic lives here (CEC invariant #5).
/// </summary>
public sealed class AcquireLeaseCommandHandler(IRunJournal journal, IClock clock)
{
    public async Task<Result<LeaseAcquired, Error>> HandleAsync(
        AcquireLeaseCommand command,
        CancellationToken ct = default)
    {
        // AC-1: enforce one-lease-per-worker.
        var hasLeaseResult = await journal.HasActiveLeaseAsync(command.WorkerId, ct);
        if (hasLeaseResult.IsFailure)
            return Result<LeaseAcquired, Error>.Fail(hasLeaseResult.Error);

        if (hasLeaseResult.Value)
            return Result<LeaseAcquired, Error>.Fail(Error.General(
                "Lease.AlreadyHeld",
                $"Worker '{command.WorkerId}' already holds an active lease. " +
                "Release or reconcile the existing lease before acquiring a new one."));

        var leaseId    = Guid.NewGuid().ToString("D");
        var acquiredAt = clock.UtcNow;

        var entry = new RunJournalEntry(
            RunId:           command.RunId,
            AttemptId:       command.AttemptId,
            LeaseId:         leaseId,
            WorkerId:        command.WorkerId,
            HeartbeatSeq:    0L,
            CheckpointAt:    acquiredAt,
            FinalResult:     null,
            IdempotencyClass: command.IdempotencyClass,
            State:           RunState.Leased);

        var writeResult = await journal.WriteEntryAsync(entry, ct);
        if (writeResult.IsFailure)
            return Result<LeaseAcquired, Error>.Fail(writeResult.Error);

        return Result<LeaseAcquired, Error>.Ok(
            new LeaseAcquired(command.RunId, command.AttemptId, leaseId, acquiredAt));
    }
}
