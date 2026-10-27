#nullable enable

using System;
using Tracto.Contract.Models;

namespace Tracto.Contract.Execution;

public record RetryAttemptRecord(
    int AttemptNumber,
    StepState OutcomeState,
    ErrorCategory? ErrorCategory,
    string? ErrorDetail,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    bool HasCommittedSideEffect,
    string? UnknownReason);
