using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Stdin;

namespace Tracto.TerminalRuntime.Application.Stdin;

/// <summary>
/// Monitors a process's standard-output stream for interactive prompts and,
/// when a prompt matches an allowlisted pattern, responds via stdin.
///
/// Contract (TER-US-012 AC-2, AC-3, AC-4):
/// <list type="bullet">
///   <item>AC-2: An allowlisted prompt matching <see cref="PromptAllowlistEntry.Pattern"/>
///         receives its <see cref="PromptAllowlistEntry.ApprovedResponse"/> within
///         <see cref="StdinPolicy.PromptTimeoutMs"/>.</item>
///   <item>AC-3: A prompt that does not match any allowlist entry within the timeout window
///         returns <c>errorCategory=TIMEOUT, detailCode=terminal.promptTimeout</c>.
///         No guessed input is sent.</item>
///   <item>AC-4: A prompt that appears to request a credential (matches the internal
///         credential-pattern detector) but is not in the allowlist causes an immediate
///         rejection — the runtime sends NO input to the process.</item>
/// </list>
/// </summary>
public interface IPromptMatcher
{
    /// <summary>
    /// Waits for an allowlisted prompt on <paramref name="stdoutReader"/> and
    /// writes the approved response to <paramref name="stdinStream"/>.
    /// </summary>
    /// <param name="stdoutReader">
    ///   Line-oriented reader attached to the process standard-output stream.
    /// </param>
    /// <param name="stdinStream">
    ///   Writable stream connected to the process standard-input pipe.
    /// </param>
    /// <param name="allowlist">
    ///   Ordered list of (pattern, approvedResponse) pairs evaluated against each stdout line.
    /// </param>
    /// <param name="policy">Size and timing bounds.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///   Ok(true) when the prompt was matched and the response written successfully;
    ///   Fail with a descriptive <see cref="Error"/> on timeout, credential rejection, or
    ///   write failure.
    /// </returns>
    Task<Result<bool, Error>> WaitForPromptAndRespondAsync(
        TextReader stdoutReader,
        Stream stdinStream,
        IReadOnlyList<PromptAllowlistEntry> allowlist,
        StdinPolicy policy,
        CancellationToken ct = default);
}
