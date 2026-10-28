using FluentAssertions;
using Moq;
using Tracto.TerminalRuntime.Application.Arguments;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Arguments;
using Tracto.TerminalRuntime.Domain.Shells;

namespace Tracto.TerminalRuntime.Unit.Tests.Arguments;

/// <summary>
/// Unit tests for <see cref="BuildProcessStartHandler"/>.
/// </summary>
public sealed class BuildProcessStartHandlerTests
{
    // Policy allows C:\data (covers both the executable and working-directory paths used below).
    private static readonly AllowedUrlRootsPolicy AnyPolicy =
        new(["api.internal"], [@"C:\data"]);

    private static BuildProcessStartCommand MakeCommand(
        IReadOnlyList<CommandArgument>? args = null,
        string executablePath = @"C:\data\tools\cmd.exe",
        string workingDir = @"C:\data\work")
    {
        return new BuildProcessStartCommand(
            Shell: ShellType.Cmd,
            ExecutablePath: executablePath,
            Arguments: args ?? [],
            WorkingDirectory: workingDir,
            EnvironmentVars: new Dictionary<string, string>(),
            UrlPolicy: AnyPolicy);
    }

    /// <summary>
    /// Sets up the mock so that <see cref="IArgumentValidator.ValidatePath"/> returns Ok
    /// for any path — used by tests that exercise later stages of the handler pipeline.
    /// </summary>
    private static void SetupValidatePathOk(Mock<IArgumentValidator> mock)
    {
        mock.Setup(v => v.ValidatePath(
                It.IsAny<string>(),
                It.IsAny<AllowedUrlRootsPolicy>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns<string, AllowedUrlRootsPolicy, string, string>(
                (path, _, _, _) => Result<string, Error>.Ok(path));
    }

    [Fact]
    public void Handle_ValidCommand_ReturnsOkWithSpec()
    {
        var mockValidator = new Mock<IArgumentValidator>();
        SetupValidatePathOk(mockValidator);
        mockValidator
            .Setup(v => v.Validate(It.IsAny<IReadOnlyList<CommandArgument>>(), It.IsAny<AllowedUrlRootsPolicy>()))
            .Returns(Result<IReadOnlyList<CommandArgument>, Error>.Ok([]));

        var handler = new BuildProcessStartHandler(mockValidator.Object);
        var result = handler.Handle(MakeCommand());

        result.IsSuccess.Should().BeTrue();
        result.Value.Shell.Should().Be(ShellType.Cmd);
    }

    [Fact]
    public void Handle_ValidatorReturnsFailure_PropagatesError()
    {
        var expectedError = Error.Validation("ARGS_010", "too long", "terminal.argumentTooLong");

        var mockValidator = new Mock<IArgumentValidator>();
        SetupValidatePathOk(mockValidator);
        mockValidator
            .Setup(v => v.Validate(It.IsAny<IReadOnlyList<CommandArgument>>(), It.IsAny<AllowedUrlRootsPolicy>()))
            .Returns(Result<IReadOnlyList<CommandArgument>, Error>.Fail(expectedError));

        var handler = new BuildProcessStartHandler(mockValidator.Object);
        var result = handler.Handle(MakeCommand());

        result.IsFailure.Should().BeTrue();
        result.Error.DetailCode.Should().Be("terminal.argumentTooLong");
    }

    [Fact]
    public void Handle_EmptyExecutablePath_ReturnsFail()
    {
        var mockValidator = new Mock<IArgumentValidator>();
        var handler = new BuildProcessStartHandler(mockValidator.Object);

        var cmd = MakeCommand(executablePath: "   ");
        var result = handler.Handle(cmd);

        result.IsFailure.Should().BeTrue();
        result.Error.DetailCode.Should().Be("terminal.missingExecutablePath");
        // Validator must NOT have been called — fail before validation.
        mockValidator.Verify(
            v => v.Validate(It.IsAny<IReadOnlyList<CommandArgument>>(), It.IsAny<AllowedUrlRootsPolicy>()),
            Times.Never);
        mockValidator.Verify(
            v => v.ValidatePath(It.IsAny<string>(), It.IsAny<AllowedUrlRootsPolicy>(),
                                It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public void Handle_EmptyWorkingDirectory_ReturnsFail()
    {
        var mockValidator = new Mock<IArgumentValidator>();
        var handler = new BuildProcessStartHandler(mockValidator.Object);

        var cmd = MakeCommand(workingDir: "");
        var result = handler.Handle(cmd);

        result.IsFailure.Should().BeTrue();
        result.Error.DetailCode.Should().Be("terminal.missingWorkingDirectory");
        // ValidatePath for WorkingDirectory must NOT have been called.
        mockValidator.Verify(
            v => v.Validate(It.IsAny<IReadOnlyList<CommandArgument>>(), It.IsAny<AllowedUrlRootsPolicy>()),
            Times.Never);
    }

    // ── MAJOR fix: ExecutablePath must be validated against the path-roots policy ──

    [Fact]
    public void Handle_ExecutablePathOutsideAllowedRoots_ReturnsFail_WithDisallowedExecutablePathCode()
    {
        var pathError = Error.Validation(
            "ARGS_020",
            "'ExecutablePath' references a filesystem path outside all approved roots.",
            "terminal.disallowedExecutablePath");

        var mockValidator = new Mock<IArgumentValidator>();
        // ValidatePath for ExecutablePath → fail
        mockValidator
            .Setup(v => v.ValidatePath(
                @"C:\Windows\System32\evil.exe",
                It.IsAny<AllowedUrlRootsPolicy>(),
                "ExecutablePath",
                "terminal.disallowedExecutablePath"))
            .Returns(Result<string, Error>.Fail(pathError));

        var handler = new BuildProcessStartHandler(mockValidator.Object);
        var cmd = MakeCommand(executablePath: @"C:\Windows\System32\evil.exe");
        var result = handler.Handle(cmd);

        result.IsFailure.Should().BeTrue();
        result.Error.DetailCode.Should().Be("terminal.disallowedExecutablePath");

        // Validate() must NOT have been called — fail at path guard stage.
        mockValidator.Verify(
            v => v.Validate(It.IsAny<IReadOnlyList<CommandArgument>>(), It.IsAny<AllowedUrlRootsPolicy>()),
            Times.Never);
    }

    // ── MAJOR fix: WorkingDirectory must be validated against the path-roots policy ──

    [Fact]
    public void Handle_WorkingDirectoryOutsideAllowedRoots_ReturnsFail_WithDisallowedWorkingDirectoryCode()
    {
        var pathError = Error.Validation(
            "ARGS_020",
            "'WorkingDirectory' references a filesystem path outside all approved roots.",
            "terminal.disallowedWorkingDirectory");

        var mockValidator = new Mock<IArgumentValidator>();
        // ValidatePath for ExecutablePath → Ok
        mockValidator
            .Setup(v => v.ValidatePath(
                It.IsAny<string>(),
                It.IsAny<AllowedUrlRootsPolicy>(),
                "ExecutablePath",
                "terminal.disallowedExecutablePath"))
            .Returns<string, AllowedUrlRootsPolicy, string, string>(
                (path, _, _, _) => Result<string, Error>.Ok(path));
        // ValidatePath for WorkingDirectory → fail
        mockValidator
            .Setup(v => v.ValidatePath(
                @"C:\sensitive\system32",
                It.IsAny<AllowedUrlRootsPolicy>(),
                "WorkingDirectory",
                "terminal.disallowedWorkingDirectory"))
            .Returns(Result<string, Error>.Fail(pathError));

        var handler = new BuildProcessStartHandler(mockValidator.Object);
        var cmd = MakeCommand(workingDir: @"C:\sensitive\system32");
        var result = handler.Handle(cmd);

        result.IsFailure.Should().BeTrue();
        result.Error.DetailCode.Should().Be("terminal.disallowedWorkingDirectory");

        // Validate() must NOT have been called.
        mockValidator.Verify(
            v => v.Validate(It.IsAny<IReadOnlyList<CommandArgument>>(), It.IsAny<AllowedUrlRootsPolicy>()),
            Times.Never);
    }

    [Fact]
    public void Handle_ArgumentValues_MappedToSpecArgumentList_InOrder()
    {
        var args = new[]
        {
            new CommandArgument("p1", "first"),
            new CommandArgument("p2", "second"),
            new CommandArgument("p3", "third"),
        };

        var mockValidator = new Mock<IArgumentValidator>();
        SetupValidatePathOk(mockValidator);
        mockValidator
            .Setup(v => v.Validate(It.IsAny<IReadOnlyList<CommandArgument>>(), It.IsAny<AllowedUrlRootsPolicy>()))
            .Returns(Result<IReadOnlyList<CommandArgument>, Error>.Ok(args));

        var handler = new BuildProcessStartHandler(mockValidator.Object);
        var result = handler.Handle(MakeCommand(args: args));

        result.IsSuccess.Should().BeTrue();
        result.Value.ArgumentList.Should().ContainInOrder("first", "second", "third");
    }
}
