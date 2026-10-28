using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Arguments;

namespace Tracto.TerminalRuntime.Application.Arguments;

/// <summary>
/// Validates a list of <see cref="CommandArgument"/> values against schema constraints
/// (max-length, URL/path allow-list) BEFORE any process is launched.
///
/// Implementations must never launch a process, perform I/O, or log the raw argument
/// values (no plaintext data in logs — AC-5, AC-6).
/// </summary>
public interface IArgumentValidator
{
    /// <summary>
    /// Validates every argument in <paramref name="arguments"/>.
    /// Returns Ok(arguments) on success.
    /// Returns Fail with <c>errorCategory=VALIDATION</c> on first violation found.
    /// </summary>
    /// <param name="arguments">The ordered set of arguments to validate.</param>
    /// <param name="urlPolicy">
    ///   Policy that lists approved URL hosts and filesystem path roots.
    ///   Any argument resembling a disallowed URL or path is rejected.
    /// </param>
    Result<IReadOnlyList<CommandArgument>, Error> Validate(
        IReadOnlyList<CommandArgument> arguments,
        AllowedUrlRootsPolicy urlPolicy);

    /// <summary>
    /// Validates a single filesystem path (such as <c>ExecutablePath</c> or
    /// <c>WorkingDirectory</c>) against <paramref name="policy"/>'s approved path roots.
    ///
    /// Rules:
    /// <list type="bullet">
    ///   <item>Empty or whitespace paths are returned as Ok — emptiness is the caller's concern.</item>
    ///   <item>Relative / bare names (e.g. <c>cmd.exe</c>, <c>pwsh.exe</c>) are not restricted here;
    ///         OS PATH resolution governs them.</item>
    ///   <item>Absolute filesystem paths must fall under one of
    ///         <see cref="AllowedUrlRootsPolicy.AllowedPathRoots"/>; otherwise Fail is returned.</item>
    /// </list>
    /// </summary>
    /// <param name="path">The path value to check.</param>
    /// <param name="policy">Policy that lists approved filesystem root prefixes.</param>
    /// <param name="paramName">Human-readable parameter name used in the error message.</param>
    /// <param name="detailCode">Machine-readable detail code placed in the returned Error.</param>
    Result<string, Error> ValidatePath(
        string path,
        AllowedUrlRootsPolicy policy,
        string paramName,
        string detailCode);
}
