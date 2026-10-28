using FluentAssertions;
using Tracto.Orchestrator.Application.Deployment;
using Tracto.Orchestrator.Domain.Settings;

namespace Tracto.Orchestrator.Unit.Tests.Deployment;

/// <summary>
/// Unit tests for <see cref="ModeParser"/>.
/// OC-16 AC: container starts in Connected/Isolated mode; invalid TRACTO_MODE fails startup.
/// No I/O, no Thread.Sleep, no framework dependencies — pure in-process logic.
/// </summary>
public sealed class ModeParserTests
{
    // ─── Happy-path: valid mode strings ────────────────────────────────────────

    [Theory]
    [InlineData("Connected",  DeploymentMode.Connected)]
    [InlineData("Isolated",   DeploymentMode.Isolated)]
    [InlineData("connected",  DeploymentMode.Connected)]
    [InlineData("isolated",   DeploymentMode.Isolated)]
    [InlineData("CONNECTED",  DeploymentMode.Connected)]
    [InlineData("ISOLATED",   DeploymentMode.Isolated)]
    [InlineData("  Connected  ", DeploymentMode.Connected)]  // leading/trailing whitespace
    [InlineData("  isolated  ",  DeploymentMode.Isolated)]
    public void Parse_ValidModeString_ReturnsSuccessWithExpectedMode(
        string rawValue, DeploymentMode expectedMode)
    {
        // Act
        var result = ModeParser.Parse(rawValue);

        // Assert
        result.IsSuccess.Should().BeTrue(because: $"'{rawValue}' is a recognised mode");
        result.Value.Should().Be(expectedMode);
    }

    // ─── Failure-path: missing or unrecognised value ───────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_NullOrWhitespace_ReturnsFailureWithNotSetMessage(string? rawValue)
    {
        // Act
        var result = ModeParser.Parse(rawValue);

        // Assert
        result.IsFailure.Should().BeTrue(because: "a missing env var must cause startup failure");
        result.Error.Message.Should()
              .Contain(ModeParser.EnvVarName,
                       because: "error message must name the offending env var");
        result.Error.Message.Should()
              .ContainAny(new[] { "not set", "Connected", "Isolated" },
                          because: "error message must tell the operator what to do");
    }

    [Theory]
    [InlineData("Online")]
    [InlineData("Standalone")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("connect")]
    [InlineData("isolate")]
    public void Parse_UnrecognisedValue_ReturnsFailureWithInvalidValueMessage(string rawValue)
    {
        // Act
        var result = ModeParser.Parse(rawValue);

        // Assert
        result.IsFailure.Should().BeTrue(because: $"'{rawValue}' is not a valid DeploymentMode");
        result.Error.Message.Should()
              .Contain(rawValue,
                       because: "error message must echo the bad value back to the operator");
        result.Error.Message.Should()
              .ContainAny(new[] { "Connected", "Isolated" },
                          because: "error message must list the valid choices");
    }

    // ─── Error metadata ────────────────────────────────────────────────────────

    [Fact]
    public void Parse_InvalidValue_ErrorCodeIsValidation()
    {
        // Act
        var result = ModeParser.Parse("bogus");

        // Assert
        result.Error.Code.Should()
              .NotBeNullOrWhiteSpace(because: "Error must carry a non-empty Code for structured logging");
    }

    // ─── EnvVarName constant ───────────────────────────────────────────────────

    [Fact]
    public void EnvVarName_IsTracktoMode()
    {
        ModeParser.EnvVarName.Should().Be("TRACTO_MODE");
    }

    // ─── Result monad semantics ────────────────────────────────────────────────

    [Fact]
    public void Parse_ValidMode_AccessingErrorThrows()
    {
        var result = ModeParser.Parse("Connected");

        result.IsSuccess.Should().BeTrue();
        var act = () => _ = result.Error;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Parse_InvalidMode_AccessingValueThrows()
    {
        var result = ModeParser.Parse("BadMode");

        result.IsFailure.Should().BeTrue();
        var act = () => _ = result.Value;
        act.Should().Throw<InvalidOperationException>();
    }
}
