namespace Tracto.TerminalRuntime.Domain.StreamCapture;

/// <summary>
/// A single labelled, sequenced unit of captured console output.
/// Data has already had CRLF normalised to LF (AC-6).
/// When encoded as UTF-8 the Data field does not exceed StreamCaptureLimits.MaxChunkBytes (AC-5).
/// </summary>
/// <param name="Label">Stream identity: "stdout" or "stderr".</param>
/// <param name="SequenceNumber">Zero-based monotonically increasing index within this stream.</param>
/// <param name="Data">Normalised text content. Empty for marker segments.</param>
/// <param name="IsEndMarker">True when this is the stream-end sentinel (AC-1).</param>
/// <param name="IsTruncationMarker">True when the capture limit was reached (AC-3 / AC-4).</param>
/// <param name="TruncationMarkerType">
/// "terminal.outputTruncated" (stdout) or "terminal.stderrTruncated" (stderr).
/// Non-null only when IsTruncationMarker is true.
/// </param>
public record StreamSegment(
    string  Label,
    int     SequenceNumber,
    string  Data,
    bool    IsEndMarker        = false,
    bool    IsTruncationMarker = false,
    string? TruncationMarkerType = null)
{
    // ── Sentinel factories ───────────────────────────────────────────────────

    /// <summary>Creates the stream-end sentinel for a given label.</summary>
    public static StreamSegment EndOf(string label, int sequenceNumber) =>
        new(label, sequenceNumber, string.Empty, IsEndMarker: true);

    /// <summary>Creates a truncation marker segment.</summary>
    public static StreamSegment TruncatedAt(string label, int sequenceNumber, string markerType) =>
        new(label, sequenceNumber, string.Empty,
            IsTruncationMarker: true, TruncationMarkerType: markerType);
}
