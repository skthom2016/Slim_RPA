#nullable enable

// Records step evidence for a run attempt. ST1-07: evidence must be recorded for every terminal step state.
// Sensitive outputs (sensitivity != NONE) must not appear in evidence records (same redaction rules as telemetry).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Tracto.Contract.Models;

namespace Tracto.Contract.Execution;

public sealed class EvidenceRecorder
{
    private readonly List<StepEvidenceRecord> _records = new();
    private readonly TimeProvider _timeProvider;

    public EvidenceRecorder(TimeProvider timeProvider) => _timeProvider = timeProvider;

    public IReadOnlyList<StepEvidenceRecord> Records => _records;

    public void Record(
        string stepId,
        StepState finalState,
        EvidenceClassification classification,
        DateTimeOffset startedAt,
        string? errorCategory,
        string? errorDetail,
        string? unknownReason,
        int attemptNumber,
        IReadOnlyDictionary<string, JsonElement>? outputs = null)
    {
        var now = _timeProvider.GetUtcNow();
        var durationMs = (long)(now - startedAt).TotalMilliseconds;
        _records.Add(new StepEvidenceRecord(
            stepId, finalState, classification, now, durationMs,
            errorCategory, errorDetail, unknownReason,
            NeedsAttention: finalState == StepState.Unknown,
            attemptNumber, outputs));
    }

    // Returns all records where NeedsAttention is true (for run-level needsAttention flag).
    public bool AnyNeedsAttention => _records.Any(r => r.NeedsAttention);
}
