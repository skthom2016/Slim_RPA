#nullable enable

using System.Collections.Concurrent;

namespace Tracto.Desktop.Protocol;

/// <summary>
/// In-process journal store that records step outcomes for reconciliation.
/// Used by <see cref="ReconcileHandler"/> to provide evidence to the Orchestrator.
/// </summary>
public sealed class JournalStore
{
    private readonly ConcurrentDictionary<string, string> _entries = new();

    /// <summary>Records a step outcome in the journal.</summary>
    /// <param name="stepId">Step identifier.</param>
    /// <param name="outcome">Serialised outcome summary.</param>
    public void Record(string stepId, string outcome) => _entries[stepId] = outcome;

    /// <summary>Returns all recorded entries as an ordered sequence (by insertion order, best-effort).</summary>
    public IReadOnlyDictionary<string, string> Entries => _entries;

    /// <summary>Returns the last recorded step identifier, or null if no entries.</summary>
    public string? LastStepId => _entries.IsEmpty ? null : _entries.Keys.Last();
}
