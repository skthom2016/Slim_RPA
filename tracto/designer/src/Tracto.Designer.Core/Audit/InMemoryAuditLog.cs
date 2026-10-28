namespace Tracto.Designer.Core.Audit;

using System.Collections.Concurrent;

public sealed class InMemoryAuditLog : IAuditLog
{
    private const int MaxEntries = 10_000;
    private readonly ConcurrentQueue<AuditEntry> _entries = new();

    public void Log(AuditEntry entry)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > MaxEntries)
            _entries.TryDequeue(out _);
    }

    public IReadOnlyList<AuditEntry> GetEntries(AuditCategory? category = null)
    {
        var all = _entries.ToArray();
        return category is null ? all : all.Where(e => e.Category == category).ToArray();
    }
}
