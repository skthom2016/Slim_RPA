#nullable enable

using Tracto.Contract.Models;
using Tracto.Contract.Models.Execution;

namespace Tracto.Contract.Testing;

/// <summary>
/// Fluent builder for <see cref="RunResult"/> with sensible defaults for tests.
/// Defaults: <see cref="RunState.Succeeded"/>, no step results, timestamps set to UTC now.
/// </summary>
public sealed class RunResultBuilder
{
    private string _runId = "test-run-" + Guid.NewGuid().ToString("N")[..8];
    private string _attemptId = "test-attempt-" + Guid.NewGuid().ToString("N")[..8];
    private string _leaseId = "test-lease-" + Guid.NewGuid().ToString("N")[..8];
    private string _workerId = "test-worker-01";
    private RunState _runState = RunState.Succeeded;
    private DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private DateTimeOffset _completedAt = DateTimeOffset.UtcNow;
    private bool? _needsAttention;
    private string? _unknownReason;
    private readonly List<StepResult> _stepResults = new();

    /// <summary>Override the run identifier.</summary>
    public RunResultBuilder WithRunId(string runId)
    {
        _runId = runId;
        return this;
    }

    /// <summary>Override the attempt identifier.</summary>
    public RunResultBuilder WithAttemptId(string attemptId)
    {
        _attemptId = attemptId;
        return this;
    }

    /// <summary>Override the terminal run state (default: <see cref="RunState.Succeeded"/>).</summary>
    public RunResultBuilder WithRunState(RunState state)
    {
        _runState = state;
        return this;
    }

    /// <summary>Append a step result.</summary>
    public RunResultBuilder WithStepResult(StepResult stepResult)
    {
        _stepResults.Add(stepResult);
        return this;
    }

    /// <summary>
    /// Set <c>needsAttention = true</c>.
    /// Must be set for any <see cref="RunState.Unknown"/> outcome per C3.
    /// </summary>
    public RunResultBuilder WithNeedsAttention()
    {
        _needsAttention = true;
        return this;
    }

    /// <summary>
    /// Set a stable <c>unknownReason</c> code.
    /// </summary>
    public RunResultBuilder WithUnknownReason(string reason)
    {
        _unknownReason = reason;
        return this;
    }

    /// <summary>Override the started-at timestamp.</summary>
    public RunResultBuilder WithStartedAt(DateTimeOffset startedAt)
    {
        _startedAt = startedAt;
        return this;
    }

    /// <summary>Override the completed-at timestamp.</summary>
    public RunResultBuilder WithCompletedAt(DateTimeOffset completedAt)
    {
        _completedAt = completedAt;
        return this;
    }

    /// <summary>Build and return the configured <see cref="RunResult"/>.</summary>
    public RunResult Build()
    {
        var duration = (long)(_completedAt - _startedAt).TotalMilliseconds;
        if (duration < 0) duration = 0;

        return new RunResult(
            RunId: _runId,
            AttemptId: _attemptId,
            LeaseId: _leaseId,
            WorkerId: _workerId,
            RunState: _runState,
            StartedAt: _startedAt,
            CompletedAt: _completedAt,
            DurationMs: duration,
            LastHeartbeatAt: null,
            StepResults: _stepResults.AsReadOnly(),
            NeedsAttention: _needsAttention,
            UnknownReason: _unknownReason);
    }
}
