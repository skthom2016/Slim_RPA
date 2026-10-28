using FluentAssertions;
using Moq;
using Tracto.TerminalRuntime.Application.Commands;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Commands;
using Tracto.TerminalRuntime.Domain.Shells;

namespace Tracto.TerminalRuntime.Unit.Tests.Commands;

/// <summary>
/// Unit tests for TER-US-006: Command Allowlist Enforcement and Integrity Verification.
/// All tests mock <see cref="ICommandRegistry"/> and <see cref="ICommandHashVerifier"/>
/// so no real filesystem access or registry loading occurs.
/// </summary>
public sealed class ValidateCommandRefHandlerTests
{
    private readonly Mock<ICommandRegistry> _registryMock = new(MockBehavior.Strict);
    private readonly Mock<ICommandHashVerifier> _hashVerifierMock = new(MockBehavior.Strict);
    private readonly ValidateCommandRefHandler _handler;

    public ValidateCommandRefHandlerTests()
    {
        _handler = new ValidateCommandRefHandler(
            _registryMock.Object,
            _hashVerifierMock.Object);
    }

    // ── AC-1: unknown commandRef → POLICY_DENIED / terminal.commandNotAllowed ──

    [Fact]
    public void Handle_UnknownCommandRef_ReturnsPolicyDenied()
    {
        // Arrange
        _registryMock
            .Setup(r => r.TryGet("cmd://unknown"))
            .Returns((CommandRegistryEntry?)null);

        // Act
        var result = _handler.Handle(
            new ValidateCommandRefCommand("cmd://unknown", ShellType.Cmd));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
        result.Error.DetailCode.Should().Be("terminal.commandNotAllowed");

        // No hash check should happen when the registry lookup fails.
        _hashVerifierMock.VerifyNoOtherCalls();
    }

    // ── AC-1: entry present but IsApproved=false → POLICY_DENIED / terminal.commandNotAllowed ──

    [Fact]
    public void Handle_DisabledCommandRef_ReturnsPolicyDenied()
    {
        // Arrange
        var entry = MakeEntry("cmd://disabled", @"C:\scripts\disabled.bat",
            "aabb" + new string('0', 60), isApproved: false, ShellType.Cmd);

        _registryMock
            .Setup(r => r.TryGet("cmd://disabled"))
            .Returns(entry);

        // Act
        var result = _handler.Handle(
            new ValidateCommandRefCommand("cmd://disabled", ShellType.Cmd));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
        result.Error.DetailCode.Should().Be("terminal.commandNotAllowed");

        // No hash check when the entry is not approved.
        _hashVerifierMock.VerifyNoOtherCalls();
    }

    // ── AC-2/AC-3: hash mismatch (e.g. edited .bat) → POLICY_DENIED / terminal.commandIntegrityFailure ──

    [Fact]
    public void Handle_HashMismatch_ReturnsPolicyDenied_IntegrityFailure()
    {
        // Arrange
        const string path = @"C:\scripts\approved.bat";
        const string pinnedHash = "deadbeef" + "00000000000000000000000000000000000000000000000000000000";
        var entry = MakeEntry("cmd://approved-bat", path, pinnedHash, isApproved: true, ShellType.Cmd);

        _registryMock
            .Setup(r => r.TryGet("cmd://approved-bat"))
            .Returns(entry);

        // File on disk has a different hash (simulates an edit).
        _hashVerifierMock
            .Setup(h => h.VerifyFileHash(path, pinnedHash))
            .Returns(false);

        // Act
        var result = _handler.Handle(
            new ValidateCommandRefCommand("cmd://approved-bat", ShellType.Cmd));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
        result.Error.DetailCode.Should().Be("terminal.commandIntegrityFailure");
    }

    // ── AC-4: wrong-runtime — requested shell not in SupportedShells ──────────

    [Fact]
    public void Handle_WrongRuntimeShell_ReturnsPolicyDenied()
    {
        // Arrange — entry supports PowerShell only
        var entry = MakeEntry("cmd://ps-script", @"C:\scripts\ps-script.ps1",
            new string('a', 64), isApproved: true, ShellType.PowerShell);

        _registryMock
            .Setup(r => r.TryGet("cmd://ps-script"))
            .Returns(entry);

        // Caller requests Cmd — wrong shell for this entry.
        var result = _handler.Handle(
            new ValidateCommandRefCommand("cmd://ps-script", ShellType.Cmd));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
        result.Error.DetailCode.Should().Be("terminal.commandNotAllowed");

        // No hash check when the shell type is wrong.
        _hashVerifierMock.VerifyNoOtherCalls();
    }

    // ── AC-4: unsigned/mixed-engine commandRef — wrong runtime altogether ─────

    [Fact]
    public void Handle_MixedEngineCommandRef_NotInRegistry_ReturnsPolicyDenied()
    {
        // A browser.* commandRef should never appear in the terminal registry.
        _registryMock
            .Setup(r => r.TryGet("browser://click-element"))
            .Returns((CommandRegistryEntry?)null);

        var result = _handler.Handle(
            new ValidateCommandRefCommand("browser://click-element", ShellType.Cmd));

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
        result.Error.DetailCode.Should().Be("terminal.commandNotAllowed");

        _hashVerifierMock.VerifyNoOtherCalls();
    }

    // ── Happy path: approved command, correct hash, matching shell ────────────

    [Fact]
    public void Handle_ApprovedCommand_CorrectHash_ReturnsSuccess()
    {
        // Arrange
        const string path = @"C:\scripts\ping-host.cmd";
        string hash = new string('a', 64);
        var entry = MakeEntry("cmd://ping-host", path, hash, isApproved: true,
            ShellType.Cmd, ShellType.PowerShell);

        _registryMock
            .Setup(r => r.TryGet("cmd://ping-host"))
            .Returns(entry);

        _hashVerifierMock
            .Setup(h => h.VerifyFileHash(path, hash))
            .Returns(true);

        // Act
        var result = _handler.Handle(
            new ValidateCommandRefCommand("cmd://ping-host", ShellType.Cmd));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Entry.CommandRef.Should().Be("cmd://ping-host");
        result.Value.VerifiedPath.Should().Be(path);
    }

    // ── Happy path: WSL-bash command, approved shell, matching hash ───────────

    [Fact]
    public void Handle_WslApprovedCommand_CorrectHash_ReturnsSuccess()
    {
        const string path = "/home/user/scripts/run-checks.sh";
        const string hash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var entry = MakeEntry("wsl://run-checks", path, hash, isApproved: true,
            ShellType.WslBash);

        _registryMock
            .Setup(r => r.TryGet("wsl://run-checks"))
            .Returns(entry);

        _hashVerifierMock
            .Setup(h => h.VerifyFileHash(path, hash))
            .Returns(true);

        var result = _handler.Handle(
            new ValidateCommandRefCommand("wsl://run-checks", ShellType.WslBash));

        result.IsSuccess.Should().BeTrue();
        result.Value.Entry.CommandRef.Should().Be("wsl://run-checks");
        result.Value.VerifiedPath.Should().Be(path);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CommandRegistryEntry MakeEntry(
        string commandRef,
        string executablePath,
        string expectedSha256,
        bool isApproved,
        params ShellType[] shells) =>
        new(commandRef, executablePath, expectedSha256, isApproved, shells);
}
