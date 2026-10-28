using Tracto.Orchestrator.Domain.Journal;

namespace Tracto.Orchestrator.Application.Abstractions;

/// <summary>
/// Append-only repository contract for <see cref="JournalEntry"/> audit records.
/// JournalEntries are written atomically alongside state mutations (Domain Invariant 5).
/// All methods participate in the ambient <see cref="IUnitOfWork"/> transaction.
/// </summary>
public interface IJournalRepository
{
    /// <summary>Appends a new journal entry. Never updates or deletes.</summary>
    Task AppendAsync(JournalEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all journal entries for a specific run, ordered by OccurredAt ascending.
    /// </summary>
    Task<IReadOnlyList<JournalEntry>> GetByRunIdAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true when at least one journal entry of <paramref name="eventType"/> exists for the given run.
    /// Used by AcceptLeaseCommandHandler to determine whether a RUN_RUNNING entry was ever written
    /// before an expired lease is classified as UNKNOWN vs re-queued.
    /// </summary>
    Task<bool> HasEntriesOfTypeAsync(Guid runId, string eventType, CancellationToken cancellationToken = default);
}
