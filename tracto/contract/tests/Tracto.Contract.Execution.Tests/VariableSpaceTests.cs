#nullable enable

using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Tracto.Contract.Execution;
using Tracto.Contract.Models;
using Tracto.Contract.Models.Envelope;
using Xunit;

namespace Tracto.Contract.Execution.Tests;

public sealed class VariableSpaceTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static VariableSpace BuildSpace(params (string name, VarDeclaration decl)[] declarations)
    {
        var dict = new Dictionary<string, VarDeclaration>(StringComparer.Ordinal);
        foreach (var (name, decl) in declarations)
            dict[name] = decl;
        return new VariableSpace(dict);
    }

    private static JsonElement StringElement(string value)
        => JsonSerializer.Deserialize<JsonElement>($"\"{value}\"");

    private static JsonElement IntElement(long value)
        => JsonSerializer.Deserialize<JsonElement>(value.ToString());

    private static JsonElement NullElement()
        => JsonSerializer.Deserialize<JsonElement>("null");

    // ── DeclaredVars / default value ─────────────────────────────────────────

    [Fact]
    public void Read_DeclaredVar_WithDefaultValue_ReturnsDefault()
    {
        var defaultEl = StringElement("hello");
        var sut = BuildSpace(("greeting", new VarDeclaration("string", DefaultValue: defaultEl)));

        var result = sut.Read("greeting");

        result.GetString().Should().Be("hello");
    }

    [Fact]
    public void DeclaredVars_ContainsAllDeclaredNames()
    {
        var sut = BuildSpace(
            ("alpha", new VarDeclaration("string")),
            ("beta", new VarDeclaration("integer")));

        sut.DeclaredVars.Should().Contain("alpha");
        sut.DeclaredVars.Should().Contain("beta");
    }

    // ── UndeclaredReference ───────────────────────────────────────────────────

    [Fact]
    public void Read_UndeclaredVar_ThrowsVariableSpaceException_WithUndeclaredReferenceCode()
    {
        var sut = BuildSpace();

        var ex = Assert.Throws<VariableSpaceException>(() => sut.Read("nonexistent"));

        ex.ErrorDetail.Should().Be(VariableErrorCodes.UndeclaredReference);
        ex.VarName.Should().Be("nonexistent");
    }

    // ── UninitializedRead ────────────────────────────────────────────────────

    [Fact]
    public void Read_UninitializedVar_NoDefault_ThrowsVariableSpaceException_WithUninitializedReadCode()
    {
        var sut = BuildSpace(("counter", new VarDeclaration("integer")));

        var ex = Assert.Throws<VariableSpaceException>(() => sut.Read("counter"));

        ex.ErrorDetail.Should().Be(VariableErrorCodes.UninitializedRead);
        ex.VarName.Should().Be("counter");
    }

    // ── Write then Read ───────────────────────────────────────────────────────

    [Fact]
    public void Write_ThenRead_ReturnsWrittenValue()
    {
        var sut = BuildSpace(("name", new VarDeclaration("string")));
        var value = StringElement("world");

        sut.Write("name", value);
        var result = sut.Read("name");

        result.GetString().Should().Be("world");
    }

    // ── TypeMismatch ─────────────────────────────────────────────────────────

    [Fact]
    public void Write_StringToIntegerVar_ThrowsVariableSpaceException_WithTypeMismatchCode()
    {
        var sut = BuildSpace(("count", new VarDeclaration("integer")));

        var ex = Assert.Throws<VariableSpaceException>(() => sut.Write("count", StringElement("not-a-number")));

        ex.ErrorDetail.Should().Be(VariableErrorCodes.TypeMismatch);
        ex.VarName.Should().Be("count");
    }

    [Fact]
    public void Write_NullToNonNullableVar_ThrowsVariableSpaceException_WithTypeMismatchCode()
    {
        var sut = BuildSpace(("name", new VarDeclaration("string", Nullable: false)));

        var ex = Assert.Throws<VariableSpaceException>(() => sut.Write("name", NullElement()));

        ex.ErrorDetail.Should().Be(VariableErrorCodes.TypeMismatch);
    }

    [Fact]
    public void Write_NullToNullableVar_Succeeds()
    {
        var sut = BuildSpace(("name", new VarDeclaration("string", Nullable: true)));

        sut.Write("name", NullElement());
        sut.IsAssigned("name").Should().BeTrue();
    }

    // ── IsAssigned ────────────────────────────────────────────────────────────

    [Fact]
    public void IsAssigned_BeforeWrite_ReturnsFalse()
    {
        var sut = BuildSpace(("x", new VarDeclaration("string")));

        sut.IsAssigned("x").Should().BeFalse();
    }

    [Fact]
    public void IsAssigned_AfterWrite_ReturnsTrue()
    {
        var sut = BuildSpace(("x", new VarDeclaration("string")));

        sut.Write("x", StringElement("something"));

        sut.IsAssigned("x").Should().BeTrue();
    }
}
