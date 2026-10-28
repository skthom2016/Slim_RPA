using FluentAssertions;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.Wsl;
using Tracto.TerminalRuntime.Domain.Policies;
using Tracto.TerminalRuntime.Infrastructure.Wsl;

namespace Tracto.TerminalRuntime.Unit.Tests.Wsl;

/// <summary>
/// Unit tests for TER-US-008: WSL Bash Script Invocation and Distro Isolation.
/// All four acceptance criteria are covered.
/// No real WSL process is launched — the validator is pure validation logic.
/// </summary>
public sealed class WslCommandValidatorTests
{
    private readonly WslCommandValidator _validator = new();

    // ── Shared policy used by happy-path and partial tests ───────────────────

    private static WslPolicy StandardPolicy() => new(
        CustomerDistro: "Ubuntu-Customer",
        CustomerUser: "automation",
        OrchestratorDistro: "Ubuntu-Orchestrator",
        ApprovedScriptRoot: "/opt/tracto/scripts");

    // ── AC-2: Orchestrator distro → POLICY_DENIED / wsl.orchestratorDistroRejected ──

    [Fact]
    public void Validate_DeclaredDistroIsOrchestratorDistro_ReturnsPolicyDenied()
    {
        // Arrange
        var policy = StandardPolicy();
        var command = new ValidateWslCommandCommand(
            DeclaredDistro: policy.OrchestratorDistro,   // targeting orchestrator
            DeclaredUser: policy.CustomerUser,
            ScriptPath: "/opt/tracto/scripts/run.sh",
            ScriptArgs: [],
            Policy: policy);

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
        result.Error.DetailCode.Should().Be("wsl.orchestratorDistroRejected");
    }

    [Theory]
    [InlineData("Ubuntu-Orchestrator", "UBUNTU-ORCHESTRATOR")]   // case-insensitive
    [InlineData("my-orch", "MY-ORCH")]
    public void Validate_OrchestratorDistroCheck_IsCaseInsensitive(
        string declaredDistro, string orchestratorDistro)
    {
        var policy = new WslPolicy(
            CustomerDistro: "Ubuntu-Customer",
            CustomerUser: "automation",
            OrchestratorDistro: orchestratorDistro,
            ApprovedScriptRoot: "/opt/scripts");

        var command = new ValidateWslCommandCommand(
            DeclaredDistro: declaredDistro,
            DeclaredUser: "automation",
            ScriptPath: "/opt/scripts/run.sh",
            ScriptArgs: [],
            Policy: policy);

        var result = _validator.Validate(command);

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
        result.Error.DetailCode.Should().Be("wsl.orchestratorDistroRejected");
    }

    // ── AC-3: Distro mismatch → POLICY_DENIED / wsl.distroMismatch ──────────

    [Fact]
    public void Validate_DeclaredDistroDoesNotMatchCustomerDistro_ReturnsPolicyDenied()
    {
        // Arrange
        var policy = StandardPolicy();
        var command = new ValidateWslCommandCommand(
            DeclaredDistro: "Ubuntu-SomeOtherTenant",   // not CustomerDistro, not OrchestratorDistro
            DeclaredUser: policy.CustomerUser,
            ScriptPath: "/opt/tracto/scripts/run.sh",
            ScriptArgs: [],
            Policy: policy);

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
        result.Error.DetailCode.Should().Be("wsl.distroMismatch");
    }

    [Fact]
    public void Validate_DeclaredDistroMatchIsNotCaseSensitiveForCustomerDistro()
    {
        // CustomerDistro comparison uses OrdinalIgnoreCase (distro names are not case-sensitive on Windows)
        var policy = StandardPolicy(); // CustomerDistro = "Ubuntu-Customer"
        var command = new ValidateWslCommandCommand(
            DeclaredDistro: "ubuntu-customer",           // same distro, different casing
            DeclaredUser: policy.CustomerUser,
            ScriptPath: "/opt/tracto/scripts/run.sh",
            ScriptArgs: [],
            Policy: policy);

        // Should pass the distro-match check (and script check)
        var result = _validator.Validate(command);

        result.IsSuccess.Should().BeTrue();
    }

    // ── AC-4: Script outside approved root → POLICY_DENIED / wsl.scriptPathDenied ──

    [Fact]
    public void Validate_ScriptPathOutsideApprovedRoot_ReturnsPolicyDenied()
    {
        // Arrange
        var policy = StandardPolicy(); // ApprovedScriptRoot = /opt/tracto/scripts
        var command = new ValidateWslCommandCommand(
            DeclaredDistro: policy.CustomerDistro,
            DeclaredUser: policy.CustomerUser,
            ScriptPath: "/tmp/evil.sh",                 // outside approved root
            ScriptArgs: [],
            Policy: policy);

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
        result.Error.DetailCode.Should().Be("wsl.scriptPathDenied");
    }

    [Theory]
    [InlineData("/opt/tracto/scripts-evil/run.sh")]     // sibling directory with shared prefix
    [InlineData("/opt/tracto")]                          // parent of root
    [InlineData("/opt/tracto/scripts/../../../etc/passwd")] // traversal attempt
    [InlineData("/home/user/scripts/run.sh")]             // unrelated path
    public void Validate_VariousPathsOutsideRoot_AreAllRejected(string scriptPath)
    {
        var policy = StandardPolicy();
        var command = new ValidateWslCommandCommand(
            DeclaredDistro: policy.CustomerDistro,
            DeclaredUser: policy.CustomerUser,
            ScriptPath: scriptPath,
            ScriptArgs: [],
            Policy: policy);

        var result = _validator.Validate(command);

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
        result.Error.DetailCode.Should().Be("wsl.scriptPathDenied");
    }

    // ── AC-1: Valid command → Ok with correct ArgumentList ───────────────────

    [Fact]
    public void Validate_ApprovedCommand_ReturnsSuccessWithArgumentList()
    {
        // Arrange
        var policy = StandardPolicy();
        var command = new ValidateWslCommandCommand(
            DeclaredDistro: policy.CustomerDistro,
            DeclaredUser: policy.CustomerUser,
            ScriptPath: "/opt/tracto/scripts/deploy.sh",
            ScriptArgs: ["--env", "production"],
            Policy: policy);

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var wslResult = result.Value;
        wslResult.WslExePath.Should().EndWith("wsl.exe");

        // ArgumentList must be: -d CustomerDistro -u CustomerUser -- scriptPath [args]
        wslResult.ArgumentList.Should().ContainInOrder(
            "-d", "Ubuntu-Customer",
            "-u", "automation",
            "--",
            "/opt/tracto/scripts/deploy.sh",
            "--env", "production");
    }

    [Fact]
    public void Validate_ApprovedCommandAtRootExactly_ReturnsSuccess()
    {
        // Script path equal to the approved root itself (edge case)
        var policy = new WslPolicy(
            CustomerDistro: "Ubuntu-Customer",
            CustomerUser: "automation",
            OrchestratorDistro: "Ubuntu-Orchestrator",
            ApprovedScriptRoot: "/opt/tracto/scripts/run.sh");

        var command = new ValidateWslCommandCommand(
            DeclaredDistro: policy.CustomerDistro,
            DeclaredUser: policy.CustomerUser,
            ScriptPath: "/opt/tracto/scripts/run.sh",
            ScriptArgs: [],
            Policy: policy);

        var result = _validator.Validate(command);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Validate_ApprovedCommandWithNoArgs_ArgumentListHasNoTrailingItems()
    {
        var policy = StandardPolicy();
        var command = new ValidateWslCommandCommand(
            DeclaredDistro: policy.CustomerDistro,
            DeclaredUser: policy.CustomerUser,
            ScriptPath: "/opt/tracto/scripts/healthcheck.sh",
            ScriptArgs: [],
            Policy: policy);

        var result = _validator.Validate(command);

        result.IsSuccess.Should().BeTrue();
        // Expected: ["-d", "Ubuntu-Customer", "-u", "automation", "--", "/opt/tracto/scripts/healthcheck.sh"]
        result.Value.ArgumentList.Should().HaveCount(6);
        result.Value.ArgumentList[^1].Should().Be("/opt/tracto/scripts/healthcheck.sh");
    }

    // ── Null-guard ───────────────────────────────────────────────────────────

    [Fact]
    public void Validate_NullCommand_ThrowsArgumentNullException()
    {
        var act = () => _validator.Validate(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
