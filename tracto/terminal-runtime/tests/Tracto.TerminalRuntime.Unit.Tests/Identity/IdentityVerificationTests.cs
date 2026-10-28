using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.Identity;
using Tracto.TerminalRuntime.Domain.Identity;
using Tracto.TerminalRuntime.Infrastructure.Identity;

namespace Tracto.TerminalRuntime.Unit.Tests.Identity;

/// <summary>
/// Unit tests for TER-US-002: Execution Identity and Privilege Enforcement.
/// All tests mock IIdentityVerifier so no real OS calls are made.
/// </summary>
public sealed class VerifyIdentityHandlerTests
{
    private readonly Mock<IIdentityVerifier> _verifierMock = new(MockBehavior.Strict);
    private readonly VerifyIdentityHandler _handler;

    public VerifyIdentityHandlerTests()
    {
        _handler = new VerifyIdentityHandler(_verifierMock.Object);
    }

    // ── AC-1: Windows identity mismatch → AUTHENTICATION / terminal.identityMismatch ──

    [Fact]
    public async Task HandleAsync_WindowsIdentityMismatch_ReturnsAuthenticationError()
    {
        // Arrange
        var policy = new IdentityPolicy(
            WindowsIdentity: "DOMAIN\\expected-user",
            WslDistro: null,
            WslUser: null,
            OrchestratorWslDistro: null);

        _verifierMock
            .Setup(v => v.VerifyWindowsIdentityAsync("DOMAIN\\expected-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail<IdentityVerificationResult>(
                Error.Authentication("IDENTITY_401",
                    "Identity mismatch.",
                    detailCode: "terminal.identityMismatch")));

        var command = new VerifyIdentityCommand(policy, "cmd");

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Authentication);
        result.Error.DetailCode.Should().Be("terminal.identityMismatch");
    }

    // ── AC-4: Orchestrator WSL distro → POLICY_DENIED / terminal.orchestratorDistroRejected ──

    [Fact]
    public async Task HandleAsync_WslTargetsOrchestratorDistro_ReturnsPolicyDenied()
    {
        // Arrange
        const string orchestratorDistro = "Ubuntu-Orchestrator";
        var policy = new IdentityPolicy(
            WindowsIdentity: null,
            WslDistro: orchestratorDistro,
            WslUser: "root",
            OrchestratorWslDistro: orchestratorDistro);

        _verifierMock
            .Setup(v => v.VerifyWslIdentityAsync(
                orchestratorDistro, "root", orchestratorDistro, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail<IdentityVerificationResult>(
                Error.PolicyDenied("IDENTITY_403",
                    "Targeting the Orchestrator distro is not permitted.",
                    detailCode: "terminal.orchestratorDistroRejected")));

        var command = new VerifyIdentityCommand(policy, "wsl-bash");

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
        result.Error.DetailCode.Should().Be("terminal.orchestratorDistroRejected");
    }

    // ── AC-3: Unapproved / unavailable distro → ENVIRONMENT / terminal.wslDistroUnavailable ──

    [Fact]
    public async Task HandleAsync_WslUnapprovedDistro_ReturnsEnvironmentError()
    {
        // Arrange
        var policy = new IdentityPolicy(
            WindowsIdentity: null,
            WslDistro: "SomeUnapprovedDistro",
            WslUser: "user",
            OrchestratorWslDistro: "Ubuntu-Orchestrator");

        _verifierMock
            .Setup(v => v.VerifyWslIdentityAsync(
                "SomeUnapprovedDistro", "user", "Ubuntu-Orchestrator", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail<IdentityVerificationResult>(
                Error.Environment("IDENTITY_404",
                    "Distro unavailable.",
                    detailCode: "terminal.wslDistroUnavailable")));

        var command = new VerifyIdentityCommand(policy, "wsl-bash");

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Environment);
        result.Error.DetailCode.Should().Be("terminal.wslDistroUnavailable");
    }

    // ── Happy path: valid Windows identity ───────────────────────────────────

    [Fact]
    public async Task HandleAsync_ValidWindowsIdentity_ReturnsSuccess()
    {
        // Arrange
        var policy = new IdentityPolicy(
            WindowsIdentity: "DOMAIN\\valid-user",
            WslDistro: null,
            WslUser: null,
            OrchestratorWslDistro: null);

        _verifierMock
            .Setup(v => v.VerifyWindowsIdentityAsync("DOMAIN\\valid-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(
                new IdentityVerificationResult("DOMAIN\\valid-user", IdentityVerificationKind.Windows)));

        var command = new VerifyIdentityCommand(policy, "powershell");

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Kind.Should().Be(IdentityVerificationKind.Windows);
    }

    // ── Happy path: valid WSL identity ───────────────────────────────────────

    [Fact]
    public async Task HandleAsync_ValidWslIdentity_ReturnsSuccess()
    {
        // Arrange
        var policy = new IdentityPolicy(
            WindowsIdentity: null,
            WslDistro: "Ubuntu-Customer",
            WslUser: "automation-user",
            OrchestratorWslDistro: "Ubuntu-Orchestrator");

        _verifierMock
            .Setup(v => v.VerifyWslIdentityAsync(
                "Ubuntu-Customer", "automation-user", "Ubuntu-Orchestrator", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(
                new IdentityVerificationResult("automation-user", IdentityVerificationKind.Wsl)));

        var command = new VerifyIdentityCommand(policy, "wsl-bash");

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Kind.Should().Be(IdentityVerificationKind.Wsl);
        result.Value.VerifiedIdentity.Should().Be("automation-user");
    }

    // ── Validation: missing WslDistro ────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_WslBash_MissingDistro_ReturnsValidationError()
    {
        // Arrange (no verifier call expected)
        var policy = new IdentityPolicy(
            WindowsIdentity: null,
            WslDistro: null,   // missing
            WslUser: "user",
            OrchestratorWslDistro: null);

        var command = new VerifyIdentityCommand(policy, "wsl-bash");

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Validation);
        result.Error.DetailCode.Should().Be("terminal.missingWslDistro");

        _verifierMock.VerifyNoOtherCalls();
    }

    // ── Validation: missing WslUser ──────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_WslBash_MissingUser_ReturnsValidationError()
    {
        // Arrange (no verifier call expected)
        var policy = new IdentityPolicy(
            WindowsIdentity: null,
            WslDistro: "Ubuntu",
            WslUser: null,     // missing
            OrchestratorWslDistro: null);

        var command = new VerifyIdentityCommand(policy, "wsl-bash");

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Validation);
        result.Error.DetailCode.Should().Be("terminal.missingWslUser");

        _verifierMock.VerifyNoOtherCalls();
    }

    // ── Validation: missing WindowsIdentity for Windows commands ─────────────

    [Fact]
    public async Task HandleAsync_Windows_MissingIdentityPolicy_ReturnsValidationError()
    {
        // Arrange (no verifier call expected)
        var policy = new IdentityPolicy(
            WindowsIdentity: null,  // missing
            WslDistro: null,
            WslUser: null,
            OrchestratorWslDistro: null);

        var command = new VerifyIdentityCommand(policy, "powershell");

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Validation);
        result.Error.DetailCode.Should().Be("terminal.missingWindowsIdentity");

        _verifierMock.VerifyNoOtherCalls();
    }
}

/// <summary>
/// Tests the pure policy-denial path in <see cref="WindowsIdentityVerifier"/> directly
/// (no subprocess needed — it returns before calling wsl).
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsIdentityVerifierPolicyTests
{
    private readonly WindowsIdentityVerifier _verifier =
        new(NullLogger<WindowsIdentityVerifier>.Instance);

    // ── AC-4: Direct verifier test — Orchestrator distro always rejected ──────

    [Fact]
    public async Task VerifyWslIdentityAsync_OrchestratorDistro_ReturnsPolicyDenied()
    {
        const string orchestratorDistro = "orchestrator-distro";

        var result = await _verifier.VerifyWslIdentityAsync(
            distroName: orchestratorDistro,
            expectedUser: "user",
            orchestratorDistroName: orchestratorDistro);

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
        result.Error.DetailCode.Should().Be("terminal.orchestratorDistroRejected");
    }

    [Theory]
    [InlineData("Ubuntu-Orchestrator", "UBUNTU-ORCHESTRATOR")]  // case-insensitive rejection
    [InlineData("my-orch-distro", "My-Orch-Distro")]
    public async Task VerifyWslIdentityAsync_OrchestratorDistro_IsCaseInsensitive(
        string distroName, string orchestratorDistro)
    {
        var result = await _verifier.VerifyWslIdentityAsync(
            distroName: distroName,
            expectedUser: "user",
            orchestratorDistroName: orchestratorDistro);

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
        result.Error.DetailCode.Should().Be("terminal.orchestratorDistroRejected");
    }

    // ── AC-3: Distro not installed → ENVIRONMENT (subprocess returns null) ────

    [Fact]
    public async Task VerifyWslIdentityAsync_DistroNotInstalled_ReturnsEnvironmentError()
    {
        // "nonexistent-distro-xyz" will not be installed on the CI/test machine
        var result = await _verifier.VerifyWslIdentityAsync(
            distroName: "nonexistent-distro-xyz-99999",
            expectedUser: "user",
            orchestratorDistroName: null);

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Environment);
        result.Error.DetailCode.Should().Be("terminal.wslDistroUnavailable");
    }
}
