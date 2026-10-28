using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Worker;

public interface IRunJournal
{
    Task OpenAsync(string runId, CancellationToken ct);
    Task AppendStepAsync(string runId, string stepId, string action, object? outcome, CancellationToken ct);
    Task CloseAsync(string runId, RunState finalState, CancellationToken ct);
    Task<IReadOnlyList<JournalEntry>> ReadAsync(string runId, CancellationToken ct);
}

public sealed record JournalEntry
{
    public required string StepId { get; init; }
    public required string Action { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public object? Outcome { get; init; }
}
