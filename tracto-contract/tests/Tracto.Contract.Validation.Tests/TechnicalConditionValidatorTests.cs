#nullable enable

using System.Text.Json;
using FluentAssertions;
using Tracto.Contract.Validation;
using Xunit;

namespace Tracto.Contract.Validation.Tests;

public sealed class TechnicalConditionValidatorTests
{
    private readonly TechnicalConditionValidator _sut = new();

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static JsonElement Parse(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    // ── Valid predicates ──────────────────────────────────────────────────────

    [Fact]
    public void ValidPredicate_StateEquals_Passes()
    {
        var condition = Parse("""{"type":"state.equals","value":"RUNNING"}""");

        var result = _sut.ValidateConditionSchema(condition);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidPredicate_ElementVisible_Passes()
    {
        var condition = Parse("""{"type":"element.visible","selector":"#btn"}""");

        var result = _sut.ValidateConditionSchema(condition);

        result.IsValid.Should().BeTrue();
    }

    // ── Valid composites ──────────────────────────────────────────────────────

    [Fact]
    public void ValidComposite_All_Passes()
    {
        var condition = Parse("""
            {
              "all": [
                {"type":"state.equals","value":"RUNNING"},
                {"type":"element.visible","selector":"#btn"}
              ]
            }
            """);

        var result = _sut.ValidateConditionSchema(condition);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidComposite_Any_Passes()
    {
        var condition = Parse("""
            {
              "any": [
                {"type":"element.exists","selector":"#modal"},
                {"type":"element.enabled","selector":"#submit"}
              ]
            }
            """);

        var result = _sut.ValidateConditionSchema(condition);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidComposite_Not_Passes()
    {
        var condition = Parse("""{"not":{"type":"element.visible","selector":"#spinner"}}""");

        var result = _sut.ValidateConditionSchema(condition);

        result.IsValid.Should().BeTrue();
    }

    // ── Disallowed predicate types ────────────────────────────────────────────

    [Fact]
    public void DisallowedPredicateType_Expression_IsRejected()
    {
        var condition = Parse("""{"type":"expression","expr":"x > 5"}""");

        var result = _sut.ValidateConditionSchema(condition);

        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle(d => d.Code == "DISALLOWED_PREDICATE_TYPE");
    }

    [Fact]
    public void DisallowedPredicateType_SqlQuery_IsRejected()
    {
        var condition = Parse("""{"type":"sql.query","query":"SELECT 1"}""");

        var result = _sut.ValidateConditionSchema(condition);

        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle(d => d.Code == "DISALLOWED_PREDICATE_TYPE");
    }

    // ── Empty composite array ─────────────────────────────────────────────────

    [Fact]
    public void EmptyCompositeArray_All_Fails()
    {
        var condition = Parse("""{"all":[]}""");

        var result = _sut.ValidateConditionSchema(condition);

        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle(d => d.Code == "EMPTY_CONDITION_ARRAY");
    }

    [Fact]
    public void EmptyCompositeArray_Any_Fails()
    {
        var condition = Parse("""{"any":[]}""");

        var result = _sut.ValidateConditionSchema(condition);

        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle(d => d.Code == "EMPTY_CONDITION_ARRAY");
    }

    // ── Nesting depth > 10 ────────────────────────────────────────────────────

    [Fact]
    public void NestingDepthExceedingTen_Fails()
    {
        // Build a "not" chain 12 levels deep — exceeds the max depth of 10.
        var condition = """{"type":"state.equals","value":"RUNNING"}""";
        for (var i = 0; i < 12; i++)
            condition = "{\"not\":" + condition + "}";

        var element = Parse(condition);
        var result = _sut.ValidateConditionSchema(element);

        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle(d => d.Code == "CONDITION_DEPTH_EXCEEDED");
    }
}
