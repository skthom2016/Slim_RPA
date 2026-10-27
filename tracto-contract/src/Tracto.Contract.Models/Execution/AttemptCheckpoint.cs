#nullable enable

namespace Tracto.Contract.Models.Execution;

public record AttemptCheckpoint(
    string AttemptId,
    string LeaseId,
    DateTimeOffset CheckpointAt,
    RunState RunState,
    string? LastCompletedStepId,
    bool SideEffectMayHaveCommitted);
