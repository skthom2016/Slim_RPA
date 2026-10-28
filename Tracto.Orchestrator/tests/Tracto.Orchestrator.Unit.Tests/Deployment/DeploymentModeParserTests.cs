using FluentAssertions;
using Tracto.Orchestrator.Domain.Deployment;
using Tracto.Orchestrator.Domain.Settings;

namespace Tracto.Orchestrator.Unit.Tests.Deployment;

/// <summary>
/// Unit tests for <see cref="DeploymentModeParser"/> and <see cref="OrchestratorSettings"/>.
/// No external I/O — pure logic tests using string arguments (no env-var side-effects).
/// </summary>
public sealed class DeploymentModeParserTests
{
    // ── Valid values ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Connected", DeploymentMode.Connected)]
    [InlineData("connected", DeploymentMode.Connected)]
    [InlineData("CONNECTED", DeploymentMode.Connected)]
    [InlineData("Isolated",  DeploymentMode.Isolated)]
    [InlineData("isolated",  DeploymentMode.Isolated)]
    [InlineData("ISOLATED",  DeploymentMode.Isolated)]
    public void Parse_WithValidModeString_ReturnsSuccessWithExpectedMode(
        string rawValue, DeploymentMode expectedMode)
    {
        var result = DeploymentModeParser.Parse(rawValue);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expectedMode);
    }

    // ── Invalid / missing values ───────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_WithMissingOrEmptyValue_ReturnsFailureWithMissingCode(string? rawValue)
    {
        var result = DeploymentModeParser.Parse(rawValue);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("TRACTO_MODE_MISSING");
        result.Error.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("standalone")]
    [InlineData("Offline")]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("connect")]
    public void Parse_WithUnrecognisedValue_ReturnsFailureWithInvalidCode(string rawValue)
    {
        var result = DeploymentModeParser.Parse(rawValue);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("TRACTO_MODE_INVALID");
        result.Error.Message.Should().Contain(rawValue);
    }

    // ── Bind address derivation (OrchestratorSettings) ────────────────────────

    [Fact]
    public void OrchestratorSettings_Connected_BindsOnAllInterfaces()
    {
        var settings = new OrchestratorSettings(
            Mode: DeploymentMode.Connected,
            ApiPort: 8080,
            DbPath: "test.db");

        settings.ListenAddress.Should().Be("0.0.0.0");
        settings.ListenUrl.Should().StartWith("http://0.0.0.0:");
    }

    [Fact]
    public void OrchestratorSettings_Isolated_BindsOnLoopbackOnly()
    {
        var settings = new OrchestratorSettings(
            Mode: DeploymentMode.Isolated,
            ApiPort: 8080,
            DbPath: "test.db");

        settings.ListenAddress.Should().Be("127.0.0.1");
        settings.ListenUrl.Should().StartWith("http://127.0.0.1:");
    }

    [Fact]
    public void OrchestratorSettings_ListenUrl_IncludesConfiguredPort()
    {
        var settings = new OrchestratorSettings(
            Mode: DeploymentMode.Connected,
            ApiPort: 9090,
            DbPath: "test.db");

        settings.ListenUrl.Should().EndWith(":9090");
    }

    // ── Result<T, E> invariants ────────────────────────────────────────────────

    [Fact]
    public void Result_Success_AccessingError_Throws()
    {
        var result = DeploymentModeParser.Parse("Connected");

        result.IsSuccess.Should().BeTrue();
        var act = () => _ = result.Error;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Result_Failure_AccessingValue_Throws()
    {
        var result = DeploymentModeParser.Parse("bogus");

        result.IsFailure.Should().BeTrue();
        var act = () => _ = result.Value;
        act.Should().Throw<InvalidOperationException>();
    }
}
