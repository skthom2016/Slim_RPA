#nullable enable
using Tracto.Orchestrator.Domain.Runs;

namespace Tracto.Orchestrator.Application.Abstractions;

// ── DTOs for admin list endpoint ──────────────────────────────────────────────
public sealed record AdminJobSummary(Guid RunId, string State, DateTimeOffset CreatedAt, bool NeedsAttention);
public sealed record AdminJobPage(int Total, IReadOnlyList<AdminJobSummary> Items);

// ── DTOs for admin detail endpoint ───────────────────────────────────────────
/// <summary>Final result view: TerminalState is a string for API projection.</summary>
public sealed record AdminFinalResultView(
    Guid            Id,
    string          TerminalState,
    string?         ErrorCategory,
    string?         ErrorMessage,
    string?         BusinessOutcome,
    string[]        EvidenceRefs,
    string?         BusinessJobId,
    DateTimeOffset  CompletedAt,
    bool            NeedsAttention,
    string?         UnknownReason);

/// <summary>Journal entry view used in admin detail response.</summary>
public sealed record AdminJournalEntryView(Guid Id, string EventType, DateTimeOffset OccurredAt, string? Payload);

/// <summary>Full admin detail view for a single run — OC-13.</summary>
public sealed record AdminJobDetailView(
    Guid                               RunId,
    string                             WorkflowId,
    string                             State,
    Guid                               AttemptId,
    DateTimeOffset                     CreatedAt,
    DateTimeOffset?                    UpdatedAt,
    bool                               NeedsAttention,
    string?                            UnknownReason,
    string?                            WorkerId,
    Guid                               CorrelationId,
    string                             ScriptArtifactId,
    string                             ScriptVersion,
    string                             SchemaVersion,
    string[]                           InputRefs,
    DateTime?                          Deadline,
    string?                            EventId,
    string                             TriggerType,
    string?                            BusinessJobId,
    AdminFinalResultView?              FinalResult,
    IReadOnlyList<AdminJournalEntryView> ResolutionEntries);

/// <summary>Admin-specific read queries for run records (OC-13).</summary>
public interface IAdminJobRepository
{
    Task<AdminJobPage> ListJobsAsync(RunState? state, DateTimeOffset? from, DateTimeOffset? to, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<AdminJobDetailView?> GetJobDetailAsync(Guid runId, CancellationToken cancellationToken = default);
}