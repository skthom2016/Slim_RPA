namespace Tracto.TerminalRuntime.Domain.StreamCapture;

/// <summary>
/// The complete (or truncated) captured output of one console stream.
/// Aggregates all StreamSegments that were emitted for a single run.
/// </summary>
/// <param name="Label">Stream identity: "stdout" or "stderr".</param>
/// <param name="Segments">
/// Ordered array of captured segments.
/// The final element is either an end-marker or a truncation marker followed by an end-marker.
/// </param>
/// <param name="IsTruncated">
/// True when the byte limit was reached before the stream ended naturally (AC-3 / AC-4).
/// </param>
/// <param name="TruncationMarker">
/// "terminal.outputTruncated" or "terminal.stderrTruncated" when IsTruncated is true; null otherwise.
/// </param>
public record CapturedStream(
    string          Label,
    StreamSegment[] Segments,
    bool            IsTruncated,
    string?         TruncationMarker = null)
{
    /// <summary>Concatenates all non-marker segment Data fields into a single string.</summary>
    public string FullText =>
        string.Concat(Segments
            .Where(s => !s.IsEndMarker && !s.IsTruncationMarker)
            .Select(s => s.Data));
}
