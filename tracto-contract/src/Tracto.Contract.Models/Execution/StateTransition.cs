#nullable enable

namespace Tracto.Contract.Models.Execution;

public record StateTransition(RunState From, RunState To);

public record TransitionResult(bool IsAllowed, string? RejectionReason = null);
