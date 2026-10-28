#nullable enable

using System.Text.Json;
using FluentAssertions;
using Tracto.Contract.Execution;
using Tracto.Contract.Models;
using Tracto.Contract.Testing;
using Xunit;

namespace Tracto.Contract.Execution.Tests;

public sealed class OnFailureHandlerTests
{
    private readonly OnFailureHandler _sut = new();

    private static JsonElement ParseJson(string json)
        => JsonSerializer.Deserialize<JsonElement>(json);

    // ── Test 1: null onFailure → Abort ────────────────────────────────────────

    [Fact]
    public void Evaluate_NullOnFailure_ReturnsAbort()
    {
        var result = _sut.Evaluate(null);

        result.Kind.Should().Be(OnFailureKind.Abort);
        result.TargetStepId.Should().BeNull();
    }

    // ── Test 2: "abort" string → Abort ────────────────────────────────────────

    [Fact]
    public void Evaluate_AbortString_ReturnsAbort()
    {
        var el = ParseJson("\"abort\"");

        var result = _sut.Evaluate(el);

        result.Kind.Should().Be(OnFailureKind.Abort);
    }

    // ── Test 3: "continue" string → Continue ──────────────────────────────────

    [Fact]
    public void Evaluate_ContinueString_ReturnsContinue()
    {
        var el = ParseJson("\"continue\"");

        var result = _sut.Evaluate(el);

        result.Kind.Should().Be(OnFailureKind.Continue);
    }

    // ── Test 4: {"goto": "step3"} → GoTo with TargetStepId = "step3" ─────────

    [Fact]
    public void Evaluate_GotoObject_ReturnsGoTo_WithTargetStepId()
    {
        var el = ParseJson("{\"goto\": \"step3\"}");

        var result = _sut.Evaluate(el);

        result.Kind.Should().Be(OnFailureKind.GoTo);
        result.TargetStepId.Should().Be("step3");
    }

    // ── Test 5: Unknown string → Abort ────────────────────────────────────────

    [Fact]
    public void Evaluate_UnknownString_ReturnsAbort()
    {
        var el = ParseJson("\"something-else\"");

        var result = _sut.Evaluate(el);

        result.Kind.Should().Be(OnFailureKind.Abort);
    }
}
