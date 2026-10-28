namespace Tracto.Designer.Core.Audit;

public sealed class CompositeAuditLog(params IAuditLog[] logs) : IAuditLog
{
    public void Log(AuditEntry entry)
    {
        foreach (var l in logs)
            l.Log(entry);
    }

    public IReadOnlyList<AuditEntry> GetEntries(AuditCategory? category = null)
    {
        // Merge entries from all constituent logs, deduplicating by Id (the same
        // entry may exist in multiple logs because Log() fans out to all of them).
        var seen = new HashSet<Guid>();
        var merged = new List<AuditEntry>();
        foreach (var l in logs)
        {
            foreach (var entry in l.GetEntries(category))
            {
                if (seen.Add(entry.Id))
                    merged.Add(entry);
            }
        }
        return merged;
    }
}
