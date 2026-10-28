using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain;
using Tracto.TerminalRuntime.Infrastructure;

namespace Tracto.TerminalRuntime.Unit.Tests;

public sealed class HeadlessCapabilityTests
{
    private static IOptions<CapabilityAdvertiserOptions> BuildOptions(
        string workerId = "worker-1",
        List<string>? shells = null,
        string version = "1.0.0")
    {
        var opts = new CapabilityAdvertiserOptions
        {
            WorkerId        = workerId,
            SupportedShells = shells ?? ["cmd", "powershell"],
            WorkerVersion   = version
        };
        return Options.Create(opts);
    }

    private static IClock FixedClock(DateTimeOffset at)
    {
        var mock = new Mock<IClock>();
        mock.Setup(c => c.UtcNow).Returns(at);
        return mock.Object;
    }

    [Fact]
    public void BuildAdvertisement_InteractiveSessionId_IsNull()
    {
        // Arrange
        var advertiser = new CapabilityAdvertiser(BuildOptions(), FixedClock(DateTimeOffset.UtcNow));

        // Act
        var result = advertiser.BuildAdvertisement();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.InteractiveSessionId.Should().BeNull(
            "the terminal worker must never hold a foreground interactive input lease");
    }

    [Fact]
    public void BuildAdvertisement_HeadlessOnly_IsTrue()
    {
        var advertiser = new CapabilityAdvertiser(BuildOptions(), FixedClock(DateTimeOffset.UtcNow));

        var result = advertiser.BuildAdvertisement();

        result.IsSuccess.Should().BeTrue();
        result.Value.HeadlessOnly.Should().BeTrue(
            "the terminal worker must not require an interactive Windows session");
    }

    [Fact]
    public void BuildAdvertisement_RuntimeType_IsTerminal()
    {
        var advertiser = new CapabilityAdvertiser(BuildOptions(), FixedClock(DateTimeOffset.UtcNow));

        var result = advertiser.BuildAdvertisement();

        result.IsSuccess.Should().BeTrue();
        result.Value.RuntimeType.Should().Be(WorkerCapabilityAdvertisement.TerminalRuntimeType);
    }

    [Fact]
    public void BuildAdvertisement_SupportedShells_IncludesCmdAndPowerShell()
    {
        var advertiser = new CapabilityAdvertiser(BuildOptions(), FixedClock(DateTimeOffset.UtcNow));

        var result = advertiser.BuildAdvertisement();

        result.IsSuccess.Should().BeTrue();
        result.Value.SupportedShells.Should().Contain("cmd");
        result.Value.SupportedShells.Should().Contain("powershell");
    }

    [Fact]
    public void BuildAdvertisement_EmptyWorkerId_ReturnsValidationError()
    {
        var advertiser = new CapabilityAdvertiser(BuildOptions(workerId: ""), FixedClock(DateTimeOffset.UtcNow));

        var result = advertiser.BuildAdvertisement();

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Validation);
        result.Error.Code.Should().Be("CAP_001");
    }

    [Fact]
    public void BuildAdvertisement_NoShells_ReturnsValidationError()
    {
        var advertiser = new CapabilityAdvertiser(BuildOptions(shells: []), FixedClock(DateTimeOffset.UtcNow));

        var result = advertiser.BuildAdvertisement();

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Validation);
        result.Error.Code.Should().Be("CAP_002");
    }

    [Fact]
    public void BuildAdvertisement_AdvertisedAt_MatchesClock()
    {
        var fixedTime = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        var advertiser = new CapabilityAdvertiser(BuildOptions(), FixedClock(fixedTime));

        var result = advertiser.BuildAdvertisement();

        result.IsSuccess.Should().BeTrue();
        result.Value.AdvertisedAt.Should().Be(fixedTime);
    }
}
