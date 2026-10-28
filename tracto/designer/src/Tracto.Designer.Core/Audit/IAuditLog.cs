namespace Tracto.Designer.Core.Audit;

public interface IAuditLog
{
    void Log(AuditEntry entry);
    IReadOnlyList<AuditEntry> GetEntries(AuditCategory? category = null);
}
