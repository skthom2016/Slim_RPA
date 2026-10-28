using FluentAssertions;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Infrastructure.OutputBindings;

namespace Tracto.TerminalRuntime.Unit.Tests.OutputBindings;

/// <summary>
/// Unit tests for <see cref="OutputBindingResolver"/> covering AC-1 and AC-2 of
/// TER-US-014: Output Bindings and Approved Result Artifacts.
/// </summary>
public sealed class OutputBindingResolverTests
{
    private readonly OutputBindingResolver _sut = new();

    // ── AC-1: JSON value passed unchanged (no domain interpretation) ─────────────

    [Fact]
    public void Resolve_ValidJson_ExtractsBindingsUnchanged()
    {
        // Arrange — stdout JSON containing a numeric, a string and a boolean value.
        // The resolver must NOT interpret any of these values.
        const string stdout = """
            {
                "amount": 99999.99,
                "status": "approved",
                "flag": true
            }
            """;
        var names = new[] { "amount", "status", "flag" };

        // Act
        var result = _sut.Resolve(stdout, names);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var bindings = result.Value;
        bindings.Should().HaveCount(3);
        bindings.First(b => b.Name == "amount").Value.Should().Be("99999.99",
            because: "AC-1 requires the raw JSON value to be passed unchanged — no rounding or conversion");
        bindings.First(b => b.Name == "status").Value.Should().Be("\"approved\"",
            because: "string JSON value includes surrounding quotes in raw form");
        bindings.First(b => b.Name == "flag").Value.Should().Be("true");
    }

    [Fact]
    public void Resolve_MissingDeclaredKey_ReturnsValidationError()
    {
        // Arrange — stdout does not contain the declared binding "missingKey".
        const string stdout = """{"presentKey": 1}""";
        var names = new[] { "missingKey" };

        // Act
        var result = _sut.Resolve(stdout, names);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Validation);
        result.Error.Code.Should().Be("terminal.outputBinding.missingKey");
    }

    [Fact]
    public void Resolve_NonJsonStdout_ReturnsValidationError()
    {
        // Arrange
        const string stdout = "not json at all";
        var names = new[] { "anyKey" };

        // Act
        var result = _sut.Resolve(stdout, names);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Validation);
        result.Error.Code.Should().Be("terminal.outputBinding.parseError");
    }

    [Fact]
    public void Resolve_JsonArrayRoot_ReturnsValidationError()
    {
        // Arrange — root is an array, not an object.
        const string stdout = "[1, 2, 3]";
        var names = new[] { "anyKey" };

        // Act
        var result = _sut.Resolve(stdout, names);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Validation);
        result.Error.Code.Should().Be("terminal.outputBinding.notAnObject");
    }

    [Fact]
    public void Resolve_EmptyBindingNames_ReturnsEmptyList()
    {
        // Arrange
        const string stdout = """{"key": "value"}""";

        // Act
        var result = _sut.Resolve(stdout, Array.Empty<string>());

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
