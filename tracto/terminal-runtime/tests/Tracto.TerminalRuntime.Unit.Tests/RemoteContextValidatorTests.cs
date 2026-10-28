using FluentAssertions;
using Microsoft.Extensions.Options;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Infrastructure;

namespace Tracto.TerminalRuntime.Unit.Tests;

public sealed class RemoteContextValidatorTests
{
    private static IOptions<RemoteContextValidatorOptions> BuildOptions(
        params string[] allowedHosts) =>
        Options.Create(new RemoteContextValidatorOptions
        {
            AllowedRemoteHosts = [.. allowedHosts]
        });

    [Fact]
    public void ValidateRemoteHost_HostNotInPolicy_ReturnsPolicyDenied()
    {
        // Arrange
        var validator = new RemoteContextValidator(BuildOptions("allowed-host.internal"));

        // Act
        var result = validator.ValidateRemoteHost("ssh://rogue-host.external");

        // Assert
        result.IsFailure.Should().BeTrue(
            "SSH to an unconfigured host must be rejected before any connection attempt");
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
        result.Error.Code.Should().Be("RCV_002");
        result.Error.Message.Should().Contain("POLICY_DENIED");
    }

    [Fact]
    public void ValidateRemoteHost_EmptyPolicy_RejectsAllHosts()
    {
        var validator = new RemoteContextValidator(BuildOptions(/* no hosts */));

        var result = validator.ValidateRemoteHost("any-host");

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
    }

    [Fact]
    public void ValidateRemoteHost_AllowedHost_ReturnsSuccess()
    {
        var validator = new RemoteContextValidator(BuildOptions("build-agent.internal"));

        var result = validator.ValidateRemoteHost("build-agent.internal");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("build-agent.internal");
    }

    [Fact]
    public void ValidateRemoteHost_AllowedHost_CaseInsensitive()
    {
        var validator = new RemoteContextValidator(BuildOptions("BUILD-AGENT.INTERNAL"));

        var result = validator.ValidateRemoteHost("build-agent.internal");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateRemoteHost_EmptyString_ReturnsValidationError()
    {
        var validator = new RemoteContextValidator(BuildOptions("allowed-host"));

        var result = validator.ValidateRemoteHost(string.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Validation);
        result.Error.Code.Should().Be("RCV_001");
    }

    [Fact]
    public void ValidateRemoteHost_WhitespaceOnly_ReturnsValidationError()
    {
        var validator = new RemoteContextValidator(BuildOptions("allowed-host"));

        var result = validator.ValidateRemoteHost("   ");

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Validation);
    }

    [Fact]
    public void ValidateRemoteHost_MultipleAllowedHosts_OnlyAllowsListed()
    {
        var validator = new RemoteContextValidator(BuildOptions("host-a", "host-b"));

        validator.ValidateRemoteHost("host-a").IsSuccess.Should().BeTrue();
        validator.ValidateRemoteHost("host-b").IsSuccess.Should().BeTrue();
        validator.ValidateRemoteHost("host-c").IsFailure.Should().BeTrue();
    }
}
