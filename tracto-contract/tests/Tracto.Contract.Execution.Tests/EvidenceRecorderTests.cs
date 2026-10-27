#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Tracto.Contract.Execution;
using Tracto.Contract.Models;
using Tracto.Contract.Testing;
using Xunit;

namespace Tracto.Contract.Execution.Tests;

public sealed class EvidenceRecorderTests
{
    // ── Test 1: Record a SUCCEEDED step — record appears in Records list ───────

    [Fact]
    public void Record_Succeeded_AppearsInRecords()
    {
        var clock = new FakeTimeProvider();
        var sut   = new EvidenceRecorder(clock);
        var start = clock.GetUtcNow();

        sut.Record(
            stepId: "step1",
            finalState: StepState.Succeeded,
            classification: EvidenceClassification.TechnicalMetadata,
            startedAt: start,
            errorCategory: null,
            errorDetail: null,
            unknownReason: null,
            attemptNumber: 1);

        sut.Records.Should().HaveCount(1);
        sut.Records[0].StepId.Should().Be("step1");
        sut.Records[0].FinalState.Should().Be(StepState.Succeeded);
    }

    // ── Test 2: Record an UNKNOWN step — NeedsAttention=true in record ─────────

    [Fact]
    public void Record_Unknown_NeedsAttentionIsTrue()
    {
        var clock = new FakeTimeProvider();
        var sut   = new EvidenceRecorder(clock);
        var start = clock.GetUtcNow();

        sut.Record(
            stepId: "step-unknown",
            finalState: StepState.Unknown,
            classification: EvidenceClassification.TechnicalMetadata,
            startedAt: start,
            errorCategory: null,
            errorDetail: null,
            unknownReason: "agent lost contact",
            attemptNumber: 1);

        sut.Records[0].NeedsAttention.Should().BeTrue();
    }

    // ── Test 3: AnyNeedsAttention returns true when any record has it ─────────

    [Fact]
    public void AnyNeedsAttention_WhenOneUnknownRecord_ReturnsTrue()
    {
        var clock = new FakeTimeProvider();
        var sut   = new EvidenceRecorder(clock);
        var start = clock.GetUtcNow();

        // First record: SUCCEEDED (NeedsAttention = false)
        sut.Record("step1", StepState.Succeeded, EvidenceClassification.TechnicalMetadata,
            start, null, null, null, 1);

        // Second record: UNKNOWN (NeedsAttention = true)
        sut.Record("step2", StepState.Unknown, EvidenceClassification.TechnicalMetadata,
            start, null, null, "reason", 1);

        sut.AnyNeedsAttention.Should().BeTrue();
    }

    // ── Test 4: DurationMs is non-negative (monotonic timing) ────────────────

    [Fact]
    public void Record_DurationMs_IsNonNegative()
    {
        var clock = new FakeTimeProvider();
        var sut   = new EvidenceRecorder(clock);
        var start = clock.GetUtcNow();

        // Advance the clock by 200ms before the record is completed
        clock.Advance(TimeSpan.FromMilliseconds(200));

        sut.Record("step1", StepState.Succeeded, EvidenceClassification.TechnicalMetadata,
            start, null, null, null, 1);

        sut.Records[0].DurationMs.Should().BeGreaterThanOrEqualTo(0);
        sut.Records[0].DurationMs.Should().Be(200);
    }

    // ── Test 5: FinalState correctly stored in record ─────────────────────────

    [Fact]
    public void Record_FinalState_IsStoredCorrectly()
    {
        var clock = new FakeTimeProvider();
        var sut   = new EvidenceRecorder(clock);
        var start = clock.GetUtcNow();

        sut.Record("step1", StepState.Failed, EvidenceClassification.TechnicalMetadata,
            start, "VALIDATION", "step.failed", null, 2);

        var record = sut.Records[0];
        record.FinalState.Should().Be(StepState.Failed);
        record.ErrorCategory.Should().Be("VALIDATION");
        record.ErrorDetail.Should().Be("step.failed");
        record.AttemptNumber.Should().Be(2);
    }
}
