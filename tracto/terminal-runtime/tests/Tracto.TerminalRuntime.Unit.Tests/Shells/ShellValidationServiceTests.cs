using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.TerminalRuntime.Application.Shells;
using Tracto.TerminalRuntime.Domain.Shells;

namespace Tracto.TerminalRuntime.Unit.Tests.Shells;

public sealed class ShellValidationServiceTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ShellValidationService CreateService(IShellDetector detector) =>
        new(detector, NullLogger<ShellValidationService>.Instance);

    private static Mock<IShellDetector> MockDetector(params ShellInfo[] shells)
    {
        var mock = new Mock<IShellDetector>();
        mock.Setup(d => d.DetectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ShellInfo>)shells.ToList());
        return mock;
    }

    // ── AC-1: Initialisation records all shell versions ───────────────────────

    [Fact]
    public async Task InitialiseAsync_ReturnsAdvertisementWithAllShells()
    {
        var shells = new[]
        {
            new ShellInfo(ShellType.Cmd, "cmd.exe", "10.0.26100", IsAvailable: true),
            new ShellInfo(ShellType.PowerShell, "pwsh.exe", "7.4.0", IsAvailable: true),
            ShellInfo.Unavailable(ShellType.WslBash)
        };

        var svc = CreateService(MockDetector(shells).Object);
        var advert = await svc.InitialiseAsync();

        advert.AvailableShells.Should().HaveCount(3);
        advert.Supports(ShellType.Cmd).Should().BeTrue();
        advert.Supports(ShellType.PowerShell).Should().BeTrue();
        advert.Supports(ShellType.WslBash).Should().BeFalse();
    }

    // ── AC-2: Job with unsupported shell rejected before execution ────────────

    [Theory]
    [InlineData(ShellType.Cmd)]
    [InlineData(ShellType.PowerShell)]
    public async Task ValidateShell_UnavailableNonWslShell_ReturnsShellUnavailableError(ShellType shellType)
    {
        var shells = new[]
        {
            ShellInfo.Unavailable(ShellType.Cmd),
            ShellInfo.Unavailable(ShellType.PowerShell),
            ShellInfo.Unavailable(ShellType.WslBash)
        };

        var svc = CreateService(MockDetector(shells).Object);
        await svc.InitialiseAsync();

        var result = svc.ValidateShell(shellType);

        result.IsFailure.Should().BeTrue();
        result.Error.DetailCode.Should().Be("terminal.shellUnavailable");
        result.Error.Kind.Should().Be(Application.Common.ErrorKind.Environment);
    }

    [Fact]
    public async Task ValidateShell_AvailableShell_ReturnsOkWithShellInfo()
    {
        var cmdInfo = new ShellInfo(ShellType.Cmd, "cmd.exe", "10.0.26100", IsAvailable: true);
        var svc = CreateService(MockDetector(cmdInfo).Object);
        await svc.InitialiseAsync();

        var result = svc.ValidateShell(ShellType.Cmd);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(cmdInfo);
    }

    // ── AC-3: WSL Bash not installed → terminal.wslUnavailable ───────────────

    [Fact]
    public async Task ValidateShell_WslUnavailable_ReturnsWslUnavailableError()
    {
        var shells = new[]
        {
            new ShellInfo(ShellType.Cmd, "cmd.exe", "10.0.26100", IsAvailable: true),
            new ShellInfo(ShellType.PowerShell, "pwsh.exe", "7.4.0", IsAvailable: true),
            ShellInfo.Unavailable(ShellType.WslBash)
        };

        var svc = CreateService(MockDetector(shells).Object);
        await svc.InitialiseAsync();

        var result = svc.ValidateShell(ShellType.WslBash);

        result.IsFailure.Should().BeTrue();
        result.Error.DetailCode.Should().Be("terminal.wslUnavailable");
        result.Error.Kind.Should().Be(Application.Common.ErrorKind.Environment);
    }

    [Fact]
    public async Task ValidateShell_WslAvailable_ReturnsOk()
    {
        var wslInfo = new ShellInfo(ShellType.WslBash, "wsl.exe", "2.3.26", IsAvailable: true);
        var svc = CreateService(MockDetector(wslInfo).Object);
        await svc.InitialiseAsync();

        var result = svc.ValidateShell(ShellType.WslBash);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(wslInfo);
    }

    // ── AC-4: Advertisement contains version information ──────────────────────

    [Fact]
    public async Task InitialiseAsync_AdvertisementIncludesVersionForAvailableShells()
    {
        var shells = new[]
        {
            new ShellInfo(ShellType.Cmd, "cmd.exe", "10.0.26100.1150", IsAvailable: true),
            new ShellInfo(ShellType.PowerShell, "pwsh.exe", "7.4.6", IsAvailable: true),
            new ShellInfo(ShellType.WslBash, "wsl.exe", "2.3.26.0", IsAvailable: true)
        };

        var svc = CreateService(MockDetector(shells).Object);
        var advert = await svc.InitialiseAsync();

        advert.For(ShellType.Cmd)!.Version.Should().Be("10.0.26100.1150");
        advert.For(ShellType.PowerShell)!.Version.Should().Be("7.4.6");
        advert.For(ShellType.WslBash)!.Version.Should().Be("2.3.26.0");
    }

    // ── Guard: calling ValidateShell before initialise returns a clear error ──

    [Fact]
    public void ValidateShell_BeforeInitialise_ReturnsGeneralError()
    {
        var svc = CreateService(MockDetector().Object);

        var result = svc.ValidateShell(ShellType.Cmd);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SHELL_001");
    }

    // ── ShellInfo.Unavailable factory ─────────────────────────────────────────

    [Fact]
    public void ShellInfo_Unavailable_IsAvailableFalse()
    {
        var info = ShellInfo.Unavailable(ShellType.PowerShell);

        info.IsAvailable.Should().BeFalse();
        info.ExecutablePath.Should().BeEmpty();
        info.Version.Should().BeEmpty();
        info.ShellType.Should().Be(ShellType.PowerShell);
    }

    // ── Result<T,Error> behaviour ─────────────────────────────────────────────

    [Fact]
    public void Result_Ok_IsSuccessTrue()
    {
        var result = Application.Common.Result.Ok(42);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Result_Fail_IsFailureTrue()
    {
        var error = Application.Common.Error.Validation("V001", "bad");
        var result = Application.Common.Result.Fail<int>(error);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("V001");
    }
}
