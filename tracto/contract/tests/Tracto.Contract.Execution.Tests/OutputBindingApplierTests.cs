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

public sealed class OutputBindingApplierTests
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

    // ── Test 1: valid outputBinding writes value to declared var ──────────────

    [Fact]
    public void Apply_ValidBinding_WritesValueToVar()
    {
        var vars = BuildVarSpace(("result", new VarDeclaration("string")));
        var sut = new OutputBindingApplier(vars);

        var outputBinding = new Dictionary<string, string> { ["result"] = "outputField" };
        var stepOutputs   = new Dictionary<string, JsonElement> { ["outputField"] = StringElement("done") };

        var errors = sut.Apply(outputBinding, stepOutputs);

        errors.Should().BeEmpty();
        vars.Read("result").GetString().Should().Be("done");
    }

    // ── Test 2: outputBinding to undeclared var returns error ─────────────────

    [Fact]
    public void Apply_UndeclaredVar_ReturnsError_UndeclaredReference()
    {
        var vars = BuildVarSpace(); // no declarations
        var sut = new OutputBindingApplier(vars);

        var outputBinding = new Dictionary<string, string> { ["missing"] = "outputField" };
        var stepOutputs   = new Dictionary<string, JsonElement> { ["outputField"] = StringElement("value") };

        var errors = sut.Apply(outputBinding, stepOutputs);

        errors.Should().HaveCount(1);
        errors[0].ErrorDetail.Should().Be("variable.undeclaredReference");
        errors[0].VarName.Should().Be("missing");
    }

    // ── Test 3: step output field doesn't exist → error ───────────────────────

    [Fact]
    public void Apply_MissingOutputField_ReturnsError_OutputFieldNotFound()
    {
        var vars = BuildVarSpace(("result", new VarDeclaration("string")));
        var sut = new OutputBindingApplier(vars);

        var outputBinding = new Dictionary<string, string> { ["result"] = "nonExistentField" };
        var stepOutputs   = new Dictionary<string, JsonElement> { ["otherField"] = StringElement("value") };

        var errors = sut.Apply(outputBinding, stepOutputs);

        errors.Should().HaveCount(1);
        errors[0].ErrorDetail.Should().Be("variable.outputFieldNotFound");
        errors[0].OutputField.Should().Be("nonExistentField");
    }

    // ── Test 4: type mismatch (string to integer var) → error ────────────────

    [Fact]
    public void Apply_TypeMismatch_StringToIntegerVar_ReturnsError_TypeMismatch()
    {
        var vars = BuildVarSpace(("count", new VarDeclaration("integer")));
        var sut = new OutputBindingApplier(vars);

        var outputBinding = new Dictionary<string, string> { ["count"] = "outputField" };
        var stepOutputs   = new Dictionary<string, JsonElement> { ["outputField"] = StringElement("not-a-number") };

        var errors = sut.Apply(outputBinding, stepOutputs);

        errors.Should().HaveCount(1);
        errors[0].ErrorDetail.Should().Be("variable.typeMismatch");
        errors[0].VarName.Should().Be("count");
    }

    // ── Test 5: multiple bindings: first succeeds, second fails ──────────────

    [Fact]
    public void Apply_MultipleBindings_FirstSucceeds_SecondFails_ReturnsOneError()
    {
        var vars = BuildVarSpace(
            ("name",  new VarDeclaration("string")),
            ("count", new VarDeclaration("integer")));
        var sut = new OutputBindingApplier(vars);

        // "name" → valid, "count" → missing output field
        var outputBinding = new Dictionary<string, string>
        {
            ["name"]  = "nameField",
            ["count"] = "missingField"
        };
        var stepOutputs = new Dictionary<string, JsonElement>
        {
            ["nameField"] = StringElement("Alice")
        };

        var errors = sut.Apply(outputBinding, stepOutputs);

        errors.Should().HaveCount(1);
        errors[0].VarName.Should().Be("count");
        vars.Read("name").GetString().Should().Be("Alice");
    }
}
