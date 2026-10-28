#nullable enable

using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Tracto.Contract.Execution;
using Tracto.Contract.Models;
using Tracto.Contract.Models.Envelope;
using Tracto.Contract.Testing;
using Xunit;

namespace Tracto.Contract.Execution.Tests;

public sealed class InputResolverTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static JsonElement StringElement(string value)
        => JsonSerializer.Deserialize<JsonElement>($"\"{value}\"");

    private static JsonElement IntElement(long value)
        => JsonSerializer.Deserialize<JsonElement>(value.ToString());

    private static VariableSpace BuildVarSpace(params (string name, VarDeclaration decl)[] declarations)
    {
        var dict = new Dictionary<string, VarDeclaration>(StringComparer.Ordinal);
        foreach (var (name, decl) in declarations)
            dict[name] = decl;
        return new VariableSpace(dict);
    }

    private static InputResolver BuildResolver(
        IReadOnlyDictionary<string, JsonElement>? inputs = null,
        IVariableSpace? vars = null,
        IStepOutputStore? store = null)
    {
        inputs ??= new Dictionary<string, JsonElement>();
        vars ??= BuildVarSpace();
        store ??= new StepOutputStore();
        return new InputResolver(inputs, vars, store);
    }

    // ── Test 1: literal value passes through unchanged (no "ref" property) ────

    [Fact]
    public void ResolveAll_LiteralValue_PassesThroughUnchanged()
    {
        var sut = BuildResolver();
        var rawInputs = new Dictionary<string, JsonElement>
        {
            ["greeting"] = StringElement("hello")
        };

        var result = sut.ResolveAll(rawInputs);

        result.HasErrors.Should().BeFalse();
        result.Resolved["greeting"].GetString().Should().Be("hello");
    }

    // ── Test 2: "inputs.name" ref resolves from run inputs dictionary ─────────

    [Fact]
    public void Resolve_InputsRef_ResolvesFromRunInputs()
    {
        var runInputs = new Dictionary<string, JsonElement>
        {
            ["name"] = StringElement("Alice")
        };
        var sut = BuildResolver(inputs: runInputs);

        var (value, error) = sut.Resolve("inputs.name");

        error.Should().BeNull();
        value!.Value.GetString().Should().Be("Alice");
    }

    // ── Test 3: "inputs.missing" ref returns InputResolutionError ─────────────

    [Fact]
    public void Resolve_InputsRef_Missing_ReturnsError_InputNotFound()
    {
        var sut = BuildResolver();

        var (value, error) = sut.Resolve("inputs.missing");

        value.Should().BeNull();
        error.Should().NotBeNull();
        error!.Detail.Should().Be("input.notFound");
    }

    // ── Test 4: "vars.x" ref resolves from VariableSpace (has default) ────────

    [Fact]
    public void Resolve_VarsRef_DeclaredWithDefault_ReturnsValue()
    {
        var defaultEl = IntElement(42);
        var vars = BuildVarSpace(("counter", new VarDeclaration("integer", DefaultValue: defaultEl)));
        var sut = BuildResolver(vars: vars);

        var (value, error) = sut.Resolve("vars.counter");

        error.Should().BeNull();
        value!.Value.GetInt64().Should().Be(42);
    }

    // ── Test 5: "vars.x" with uninitialized var returns InputResolutionError ──

    [Fact]
    public void Resolve_VarsRef_Uninitialized_ReturnsError_UninitializedRead()
    {
        var vars = BuildVarSpace(("counter", new VarDeclaration("integer")));
        var sut = BuildResolver(vars: vars);

        var (value, error) = sut.Resolve("vars.counter");

        value.Should().BeNull();
        error.Should().NotBeNull();
        error!.Detail.Should().Be("variable.uninitializedRead");
    }

    // ── Test 6: "vars.undeclared" ref returns InputResolutionError ────────────

    [Fact]
    public void Resolve_VarsRef_Undeclared_ReturnsError_UndeclaredReference()
    {
        var sut = BuildResolver();

        var (value, error) = sut.Resolve("vars.undeclared");

        value.Should().BeNull();
        error.Should().NotBeNull();
        error!.Detail.Should().Be("variable.undeclaredReference");
    }

    // ── Test 7: "steps.step1.outputs.value" resolves from StepOutputStore ─────

    [Fact]
    public void Resolve_StepsOutputsRef_Resolves_FromOutputStore()
    {
        var store = new StepOutputStore();
        store.RecordOutputs("step1",
            new Dictionary<string, JsonElement> { ["value"] = StringElement("result-data") },
            StepState.Succeeded);
        var sut = BuildResolver(store: store);

        var (value, error) = sut.Resolve("steps.step1.outputs.value");

        error.Should().BeNull();
        value!.Value.GetString().Should().Be("result-data");
    }

    // ── Test 8: "steps.step1.outputs.missing" returns error ──────────────────

    [Fact]
    public void Resolve_StepsOutputsRef_MissingField_ReturnsError_StepOutputNotFound()
    {
        var store = new StepOutputStore();
        store.RecordOutputs("step1",
            new Dictionary<string, JsonElement> { ["value"] = StringElement("data") },
            StepState.Succeeded);
        var sut = BuildResolver(store: store);

        var (value, error) = sut.Resolve("steps.step1.outputs.missing");

        value.Should().BeNull();
        error.Should().NotBeNull();
        error!.Detail.Should().Be("input.stepOutputNotFound");
    }

    // ── Test 9: "unknown.path" ref returns InputResolutionError ──────────────

    [Fact]
    public void Resolve_UnknownRefPath_ReturnsError_UnknownRefPath()
    {
        var sut = BuildResolver();

        var (value, error) = sut.Resolve("unknown.path");

        value.Should().BeNull();
        error.Should().NotBeNull();
        error!.Detail.Should().Be("input.unknownRefPath");
    }

    // ── Test 10: ResolveAll handles mix of literal and ref values ────────────

    [Fact]
    public void ResolveAll_MixedLiteralAndRef_ResolvesAll()
    {
        var runInputs = new Dictionary<string, JsonElement>
        {
            ["userId"] = StringElement("user-123")
        };
        var sut = BuildResolver(inputs: runInputs);

        // literal key "greeting", ref key "id"
        var refObj = JsonSerializer.Deserialize<JsonElement>("{\"ref\":\"inputs.userId\"}");
        var rawInputs = new Dictionary<string, JsonElement>
        {
            ["greeting"] = StringElement("hello"),
            ["id"]       = refObj
        };

        var result = sut.ResolveAll(rawInputs);

        result.HasErrors.Should().BeFalse();
        result.Resolved["greeting"].GetString().Should().Be("hello");
        result.Resolved["id"].GetString().Should().Be("user-123");
    }
}
