using Tracto.TerminalRuntime.Domain.StreamCapture;

namespace Tracto.TerminalRuntime.Application.StreamCapture;

/// <summary>
/// Abstraction for bounded, deadlock-safe stdout/stderr capture from a running process.
///
/// AC-1: ReadStdoutAsync / ReadStderrAsync yield successive segments as data arrives
///       and emit an end-marker segment after the stream closes.
/// AC-2: Stdout and stderr are captured as separate, independently readable streams.
/// AC-7: GetExitCodeAsync completes independently of how much stdout has been consumed.
/// AC-8: Encoding is declared at construction; non-ASCII content round-trips correctly.
/// </summary>
public interface IStreamCapture : IAsyncDisposable
{
    /// <summary>
    /// Returns an async stream of labelled, sequenced stdout segments.
    /// Blocks between segments when no data is ready.
    /// After the underlying process stream closes, emits any remaining buffered segments
    /// followed by a single end-marker (IsEndMarker = true).
    /// </summary>
    IAsyncEnumerable<StreamSegment> ReadStdoutAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns an async stream of labelled, sequenced stderr segments.
    /// Runs independently of ReadStdoutAsync — a large stderr burst does not block stdout (AC-2).
    /// </summary>
    IAsyncEnumerable<StreamSegment> ReadStderrAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the process exit code (AC-7).
    /// Completes as soon as the process exits, regardless of whether the caller
    /// has finished reading stdout or stderr.
    /// </summary>
    Task<int> GetExitCodeAsync(CancellationToken ct = default);
}
