using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Arguments;

namespace Tracto.TerminalRuntime.Application.Arguments;

/// <summary>
/// Application-layer handler that validates typed arguments and produces a
/// <see cref="ProcessStartSpec"/> ready for Infrastructure to launch.
///
/// INVARIANTS:
/// <list type="bullet">
///   <item>No process launching logic in this class — that belongs to Infrastructure.</item>
///   <item>No raw argument values in log messages or returned error messages.</item>
///   <item><c>ExecutablePath</c> and <c>WorkingDirectory</c> are validated against the
///         caller-supplied <see cref="AllowedUrlRootsPolicy"/> before the spec is built.
///         (MAJOR fix: all file-path I/O must be guarded by an approved-root check.)</item>
///   <item>Shell metacharacters in argument values are harmless because the
///         resulting <see cref="ProcessStartSpec.ArgumentList"/> is routed through
///         environment variables, not concatenated into a shell command string.</item>
/// </list>
/// </summary>
public sealed class BuildProcessStartHandler(IArgumentValidator argumentValidator)
{
    /// <summary>
    /// Validates all inputs in <paramref name="command"/> and builds a
    /// <see cref="ProcessStartSpec"/> on success.
    /// </summary>
    public Result<ProcessStartSpec, Error> Handle(BuildProcessStartCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // ── Step 1: Non-empty guards (fail before policy checks) ─────────────

        if (string.IsNullOrWhiteSpace(command.ExecutablePath))
            return Result.Fail<ProcessStartSpec>(
                Error.Validation(
                    "ARGS_001",
                    "ExecutablePath must not be empty.",
                    "terminal.missingExecutablePath"));

        if (string.IsNullOrWhiteSpace(command.WorkingDirectory))
            return Result.Fail<ProcessStartSpec>(
                Error.Validation(
                    "ARGS_002",
                    "WorkingDirectory must not be empty.",
                    "terminal.missingWorkingDirectory"));

        // ── Step 2: Path allow-list checks (MAJOR fix) ───────────────────────
        //
        // Every file path that reaches I/O (psi.FileName, psi.WorkingDirectory) must
        // be validated against the caller's approved-root policy before the spec is built.

        var exePathCheck = argumentValidator.ValidatePath(
            command.ExecutablePath,
            command.UrlPolicy,
            "ExecutablePath",
            "terminal.disallowedExecutablePath");

        if (exePathCheck.IsFailure)
            return Result.Fail<ProcessStartSpec>(exePathCheck.Error);

        var workDirCheck = argumentValidator.ValidatePath(
            command.WorkingDirectory,
            command.UrlPolicy,
            "WorkingDirectory",
            "terminal.disallowedWorkingDirectory");

        if (workDirCheck.IsFailure)
            return Result.Fail<ProcessStartSpec>(workDirCheck.Error);

        // ── Step 3: Argument schema validation (max-length, URL/path allow-list) ──

        var validationResult = argumentValidator.Validate(command.Arguments, command.UrlPolicy);
        if (validationResult.IsFailure)
            return Result.Fail<ProcessStartSpec>(validationResult.Error);

        // ── Step 4: Build spec ───────────────────────────────────────────────
        //
        // Extract the plain string values in declaration order.
        // These values are routed through environment variables by Infrastructure —
        // they are never concatenated into a shell command string.

        var argValues = validationResult.Value
            .Select(a => a.Value)
            .ToList()
            .AsReadOnly();

        var spec = new ProcessStartSpec(
            Shell: command.Shell,
            ExecutablePath: command.ExecutablePath,
            ArgumentList: argValues,
            WorkingDirectory: command.WorkingDirectory,
            EnvironmentVars: command.EnvironmentVars);

        return Result.Ok(spec);
    }
}
