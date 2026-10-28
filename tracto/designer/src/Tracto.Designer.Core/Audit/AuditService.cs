namespace Tracto.Designer.Core.Audit;

public sealed class AuditService(IAuditLog log)
{
    private const string Actor = "designer";

    public void LogRecordingStarted(string sessionId) =>
        log.Log(new(Guid.NewGuid(), DateTimeOffset.UtcNow, Actor, "RecordingStarted", AuditCategory.Recording, AuditOutcome.Success, SessionId: sessionId));

    public void LogRecordingStopped(string sessionId, int eventCount) =>
        log.Log(new(Guid.NewGuid(), DateTimeOffset.UtcNow, Actor, "RecordingStopped", AuditCategory.Recording, AuditOutcome.Success, $"{eventCount} events captured", sessionId));

    public void LogScriptValidated(string scriptId, bool passed, int errorCount) =>
        log.Log(new(Guid.NewGuid(), DateTimeOffset.UtcNow, Actor, "ScriptValidated", AuditCategory.Validation,
            passed ? AuditOutcome.Success : AuditOutcome.Failure, $"Script '{scriptId}': {errorCount} errors"));

    public void LogScriptSigned(string scriptId, string signerId) =>
        log.Log(new(Guid.NewGuid(), DateTimeOffset.UtcNow, Actor, "ScriptSigned", AuditCategory.Signing, AuditOutcome.Success, $"Script '{scriptId}' signed by '{signerId}'"));

    public void LogScriptPublished(string scriptId, string version, string? pubId, bool success) =>
        log.Log(new(Guid.NewGuid(), DateTimeOffset.UtcNow, Actor, "ScriptPublished", AuditCategory.Publication,
            success ? AuditOutcome.Success : AuditOutcome.Failure, $"Script '{scriptId}' v{version}, pubId={pubId}"));

    public void LogScriptExported(string scriptId, string format, string path) =>
        log.Log(new(Guid.NewGuid(), DateTimeOffset.UtcNow, Actor, "ScriptExported", AuditCategory.Export, AuditOutcome.Success, $"'{scriptId}' → {format} → {path}"));

    public void LogScriptImported(string scriptId, string path) =>
        log.Log(new(Guid.NewGuid(), DateTimeOffset.UtcNow, Actor, "ScriptImported", AuditCategory.Import, AuditOutcome.Success, $"'{scriptId}' ← {path}"));

    public void LogMcpToolCall(string tool, bool success) =>
        log.Log(new(Guid.NewGuid(), DateTimeOffset.UtcNow, Actor, $"McpTool:{tool}", AuditCategory.Mcp,
            success ? AuditOutcome.Success : AuditOutcome.Failure));

    public IReadOnlyList<AuditEntry> GetEntries(AuditCategory? category = null) =>
        log.GetEntries(category);
}
