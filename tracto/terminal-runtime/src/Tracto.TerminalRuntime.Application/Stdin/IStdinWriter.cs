using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Stdin;

namespace Tracto.TerminalRuntime.Application.Stdin;

/// <summary>
/// Writes a bounded, pre-approved payload to a process's standard-input stream.
///
/// Contract (TER-US-012 AC-1, AC-5):
/// <list type="bullet">
///   <item>The <paramref name="value"/> parameter is NEVER echoed to any log, journal entry,
///         evidence field, or Orchestrator payload (CEC invariant #4).</item>
///   <item>If <paramref name="value"/> exceeds <see cref="StdinPolicy.MaxBytes"/> bytes when
///         UTF-8-encoded, the write is rejected before touching the stream and returns
///         <c>errorCategory=VALIDATION, detailCode=terminal.stdinSizeExceeded</c>.</item>
///   <item>If the target process has already exited (pipe is closed), returns
///         <c>errorCategory=ENVIRONMENT, detailCode=terminal.processExited</c>.</item>
/// </list>
///
/// Implementations must not launch processes or call third-party APIs directly
/// (CEC invariant #5).
/// </summary>
public interface IStdinWriter
{
    /// <summary>
    /// Writes <paramref name="value"/> (UTF-8, newline-terminated) to
    /// <paramref name="stdinStream"/> subject to <paramref name="policy"/> limits.
    /// </summary>
    /// <param name="stdinStream">The writable stream connected to the process stdin pipe.</param>
    /// <param name="value">The pre-approved response string to write. Never logged.</param>
    /// <param name="policy">Size and timing bounds.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///   <see cref="Result{T,TError}"/> Ok(true) on success; Fail with an <see cref="Error"/>
    ///   describing the rejection otherwise.
    /// </returns>
    Task<Result<bool, Error>> WriteAsync(
        Stream stdinStream,
        string value,
        StdinPolicy policy,
        CancellationToken ct = default);
}
