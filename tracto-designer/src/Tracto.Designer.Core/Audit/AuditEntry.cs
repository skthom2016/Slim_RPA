namespace Tracto.Designer.Core.Audit;

public sealed record AuditEntry(
    Guid Id,
    DateTimeOffset Timestamp,
    string Actor,
    string Action,
    AuditCategory Category,
    AuditOutcome Outcome,
    string? Details = null,
    string? SessionId = null);
