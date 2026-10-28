namespace Tracto.TerminalRuntime.Domain.StreamCapture;

/// <summary>
/// Immutable configuration for bounded stdout/stderr capture.
/// All byte limits are applied to the raw encoded byte stream received from the process pipe.
/// </summary>
/// <param name="MaxStdoutBytes">
/// Maximum total bytes captured from stdout before the stream is truncated (default 10 MB).
/// AC-3: when exceeded, a terminal.outputTruncated marker is written and capture stops.
/// </param>
/// <param name="MaxStderrBytes">
/// Maximum total bytes captured from stderr before the stream is truncated (default 1 MB).
/// AC-4: when exceeded, a terminal.stderrTruncated marker is written and capture stops.
/// </param>
/// <param name="MaxChunkBytes">
/// Maximum encoded byte size of a single emitted StreamSegment (default 64 KB).
/// AC-5: oversized pipe reads are split at this boundary before being published.
/// </param>
public record StreamCaptureLimits(
    long MaxStdoutBytes = 10_485_760,
    long MaxStderrBytes = 1_048_576,
    int  MaxChunkBytes  = 65_536)
{
    /// <summary>Default limits instance, matching the AC values.</summary>
    public static readonly StreamCaptureLimits Default = new();
}
