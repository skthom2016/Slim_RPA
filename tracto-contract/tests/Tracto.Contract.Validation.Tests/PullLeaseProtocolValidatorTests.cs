#nullable enable

using System.Text.Json;
using FluentAssertions;
using Tracto.Contract.Validation;
using Xunit;

namespace Tracto.Contract.Validation.Tests;

/// <summary>
/// Tests for <see cref="PullLeaseProtocolValidator.ValidateStepResultSemantics"/> (C3 + REF-09).
/// Test JSON is built via <see cref="JsonDocument.Parse"/> as required by the task spec.
/// No mocking; uses a shared <see cref="SchemaTestFixture"/> for the schema validator.
/// </summary>
public sealed class PullLeaseProtocolValidatorTests : IClassFixture<SchemaTestFixture>
{
    private readonly PullLeaseProtocolValidator _sut;

    public PullLeaseProtocolValidatorTests(SchemaTestFixture fixture)
    {
        _sut = new PullLeaseProtocolValidator(fixture.SchemaValidator);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static JsonElement Parse(string json)
        => JsonDocument.Parse(json).RootElement;

    // ── TC1: UNKNOWN + needsAttention=true + unknownReason + lastSeenAttemptId → passes ─

    [Fact]
    public void StepResult_Unknown_AllFieldsPresent_Passes()
    {
        var msg = Parse("""
            {
              "messageType": "STEP_RESULT",
              "lastSeenAttemptId": "attempt-001",
              "inlineResult": {
                "runState":       "UNKNOWN",
                "needsAttention": true,
                "unknownReason":  "Worker lost connectivity during execution"
              }
            }
            """);

        var result = _sut.ValidateStepResultSemantics(msg);

        result.IsValid.Should().BeTrue();
        result.Diagnostics.Should().BeEmpty();
    }

    // ── TC2: UNKNOWN + needsAttention=false → UNKNOWN_MISSING_NEEDS_ATTENTION ─

    [Fact]
    public void StepResult_Unknown_NeedsAttentionFalse_FailsWithUnknownMissingNeedsAttention()
    {
        var msg = Parse("""
            {
              "messageType": "STEP_RESULT",
              "lastSeenAttemptId": "attempt-002",
              "inlineResult": {
                "runState":       "UNKNOWN",
                "needsAttention": false,
                "unknownReason":  "Some reason"
              }
            }
            """);

        var result = _sut.ValidateStepResultSemantics(msg);

        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle(d => d.Code == "UNKNOWN_MISSING_NEEDS_ATTENTION");
    }

    // ── TC3: UNKNOWN + no unknownReason → UNKNOWN_MISSING_REASON ─────────────

    [Fact]
    public void StepResult_Unknown_NoUnknownReason_FailsWithUnknownMissingReason()
    {
        var msg = Parse("""
            {
              "messageType": "STEP_RESULT",
              "lastSeenAttemptId": "attempt-003",
              "inlineResult": {
                "runState":       "UNKNOWN",
                "needsAttention": true
              }
            }
            """);

        var result = _sut.ValidateStepResultSemantics(msg);

        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle(d => d.Code == "UNKNOWN_MISSING_REASON");
    }

    // ── TC4: SUCCEEDED → no C3 diagnostics ───────────────────────────────────

    [Fact]
    public void StepResult_Succeeded_NoDiagnostics()
    {
        var msg = Parse("""
            {
              "messageType": "STEP_RESULT",
              "lastSeenAttemptId": "attempt-004",
              "inlineResult": {
                "runState": "SUCCEEDED"
              }
            }
            """);

        var result = _sut.ValidateStepResultSemantics(msg);

        result.Diagnostics.Should().NotContain(d =>
            d.Code == "UNKNOWN_MISSING_NEEDS_ATTENTION" ||
            d.Code == "UNKNOWN_MISSING_REASON",
            because: "SUCCEEDED state should not trigger C3 UNKNOWN diagnostics");
    }

    // ── TC5: STEP_RESULT missing lastSeenAttemptId → MISSING_LAST_SEEN_ATTEMPT_ID warning ─

    [Fact]
    public void StepResult_MissingLastSeenAttemptId_ProducesMissingLastSeenAttemptIdWarning()
    {
        var msg = Parse("""
            {
              "messageType": "STEP_RESULT",
              "inlineResult": {
                "runState": "SUCCEEDED"
              }
            }
            """);

        var result = _sut.ValidateStepResultSemantics(msg);

        result.Diagnostics.Should().ContainSingle(d =>
            d.Code     == "MISSING_LAST_SEEN_ATTEMPT_ID" &&
            d.Severity == ValidationSeverity.Warning,
            because: "lastSeenAttemptId is required for reconciliation (REF-09) and its absence is a warning");
    }
}
