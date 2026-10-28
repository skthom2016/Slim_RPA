#nullable enable

// Records the evidence produced by a completed step for ST1-07 (evidence recording).

using System;
using System.Collections.Generic;
using System.Text.Json;
using Tracto.Contract.Models;

namespace Tracto.Contract.Execution;

public record StepEvidenceRecord(
    string StepId,
    StepState FinalState,
    EvidenceClassification Classification,
    DateTimeOffset CompletedAt,
    long DurationMs,          // monotonic timing from TimeProvider (REF-10)
    string? ErrorCategory,
    string? ErrorDetail,
    string? UnknownReason,
    bool NeedsAttention,
    int AttemptNumber,
    IReadOnlyDictionary<string, JsonElement>? Outputs);
