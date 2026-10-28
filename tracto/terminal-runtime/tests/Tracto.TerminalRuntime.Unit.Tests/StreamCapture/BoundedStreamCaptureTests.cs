using System.Text;
using FluentAssertions;
using Tracto.TerminalRuntime.Domain.StreamCapture;
using Tracto.TerminalRuntime.Infrastructure.StreamCapture;

namespace Tracto.TerminalRuntime.Unit.Tests.StreamCapture;

/// <summary>
/// Unit tests for TER-US-011: Bounded stdout/stderr Capture and Deadlock Prevention.
///
/// All tests use in-memory readers so no real process is launched.
/// The architectural deadlock-prevention property (two independent tasks) is exercised
/// by the concurrent-drain test; a deterministic timeout guards against regressions.
/// </summary>
public sealed class BoundedStreamCaptureTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static BoundedStreamCapture MakeCapture(
        string stdoutContent,
        string stderrContent,
        StreamCaptureLimits? limits = null,
        int exitCode = 0,
        Encoding? encoding = null)
    {
        var enc = encoding ?? Encoding.UTF8;
        return new BoundedStreamCapture(
            new StringReader(stdoutContent),
            new StringReader(stderrContent),
            _ => Task.FromResult(exitCode),
            limits ?? StreamCaptureLimits.Default,
            enc);
    }

    private static async Task<List<StreamSegment>> DrainAsync(
        IAsyncEnumerable<StreamSegment> source,
        CancellationToken ct = default)
    {
        var segments = new List<StreamSegment>();
        await foreach (var seg in source.WithCancellation(ct))
            segments.Add(seg);
        return segments;
    }

    private static string GenerateRepeating(char ch, int byteCount) =>
        new string(ch, byteCount);          // ASCII: 1 char == 1 byte

    // ── AC-1: stream-end marker emitted after process exit ───────────────────

    [Fact]
    public async Task ReadStdoutAsync_WhenStreamEmpty_EmitsEndMarker()
    {
        await using var capture = MakeCapture(string.Empty, string.Empty);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var segments = await DrainAsync(capture.ReadStdoutAsync(cts.Token), cts.Token);

        segments.Should().ContainSingle(s => s.IsEndMarker);
        segments.Single(s => s.IsEndMarker).Label.Should().Be("stdout");
    }

    [Fact]
    public async Task ReadStderrAsync_WhenStreamEmpty_EmitsEndMarker()
    {
        await using var capture = MakeCapture(string.Empty, string.Empty);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var segments = await DrainAsync(capture.ReadStderrAsync(cts.Token), cts.Token);

        segments.Should().ContainSingle(s => s.IsEndMarker);
        segments.Single(s => s.IsEndMarker).Label.Should().Be("stderr");
    }

    [Fact]
    public async Task ReadStdoutAsync_WithContent_DataSegmentsPrecedeEndMarker()
    {
        await using var capture = MakeCapture("hello world", string.Empty);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var segments = await DrainAsync(capture.ReadStdoutAsync(cts.Token), cts.Token);

        var dataSegments = segments.Where(s => !s.IsEndMarker && !s.IsTruncationMarker).ToList();
        dataSegments.Should().NotBeEmpty();
        string.Concat(dataSegments.Select(s => s.Data)).Should().Be("hello world");

        // End marker must be last
        segments.Last().IsEndMarker.Should().BeTrue();
    }

    // ── AC-2: deadlock prevention — stderr flood does not block stdout ────────

    [Fact]
    public async Task Capture_LargeStderrAndSmallStdout_BothCompleteWithinTimeout()
    {
        // stderr content is well over 1 MB to trigger the limit path.
        // stdout is small.  With separate background tasks the large stderr
        // does not prevent stdout from completing.
        var largeStderr = GenerateRepeating('E', 1_200_000);  // 1.2 MB ASCII
        var stdoutText  = "stdout-done";

        await using var capture = MakeCapture(stdoutText, largeStderr);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Read both concurrently (as a real consumer would).
        var stdoutTask = DrainAsync(capture.ReadStdoutAsync(cts.Token), cts.Token);
        var stderrTask = DrainAsync(capture.ReadStderrAsync(cts.Token), cts.Token);

        await Task.WhenAll(stdoutTask, stderrTask);

        // Both tasks are completed after WhenAll; await them individually to avoid
        // xUnit1031 (no blocking .Result access).
        var stdoutSegs = await stdoutTask;
        stdoutSegs.Should().Contain(s => !s.IsEndMarker && s.Data.Contains("stdout-done"),
            "stdout data must be captured regardless of stderr size");

        // stderr must be truncated (>1 MB)
        var stderrSegs = await stderrTask;
        stderrSegs.Should().Contain(s => s.IsTruncationMarker,
            "1.2 MB stderr must produce a truncation marker");
    }

    // ── AC-2: separate, labelled streams ─────────────────────────────────────

    [Fact]
    public async Task Capture_StdoutAndStderrHaveSeparateLabels()
    {
        await using var capture = MakeCapture("out-line", "err-line");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var stdoutSegs = await DrainAsync(capture.ReadStdoutAsync(cts.Token), cts.Token);
        var stderrSegs = await DrainAsync(capture.ReadStderrAsync(cts.Token), cts.Token);

        stdoutSegs.Should().OnlyContain(s => s.Label == "stdout");
        stderrSegs.Should().OnlyContain(s => s.Label == "stderr");
    }

    // ── AC-3: stdout truncation at 10 MB ─────────────────────────────────────

    [Fact]
    public async Task ReadStdoutAsync_Over10MB_EmitsTruncationMarkerAndRunContinues()
    {
        // 11 MB of ASCII 'X'
        var largeStdout = GenerateRepeating('X', 11_000_000);

        var limits = new StreamCaptureLimits(MaxStdoutBytes: 10_485_760); // 10 MB default
        await using var capture = MakeCapture(largeStdout, string.Empty, limits);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var segments = await DrainAsync(capture.ReadStdoutAsync(cts.Token), cts.Token);

        // Truncation marker must be present with the correct type string.
        var truncMarker = segments.FirstOrDefault(s => s.IsTruncationMarker);
        truncMarker.Should().NotBeNull("stdout > 10 MB must produce a truncation marker");
        truncMarker!.TruncationMarkerType.Should().Be(BoundedStreamCapture.StdoutTruncationMarker);

        // Total captured data must not exceed 10 MB.
        long captured = segments
            .Where(s => !s.IsEndMarker && !s.IsTruncationMarker)
            .Sum(s => (long)Encoding.UTF8.GetByteCount(s.Data));
        captured.Should().BeLessThanOrEqualTo(10_485_760,
            "captured stdout bytes must not exceed 10 MB");

        // End marker must still be emitted (run continues).
        segments.Last().IsEndMarker.Should().BeTrue();
    }

    // ── AC-4: stderr truncation at 1 MB ──────────────────────────────────────

    [Fact]
    public async Task ReadStderrAsync_Over1MB_EmitsTruncationMarkerAndRunContinues()
    {
        var largeStderr = GenerateRepeating('E', 2_000_000); // 2 MB

        var limits = new StreamCaptureLimits(MaxStderrBytes: 1_048_576); // 1 MB default
        await using var capture = MakeCapture(string.Empty, largeStderr, limits);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var segments = await DrainAsync(capture.ReadStderrAsync(cts.Token), cts.Token);

        var truncMarker = segments.FirstOrDefault(s => s.IsTruncationMarker);
        truncMarker.Should().NotBeNull("stderr > 1 MB must produce a truncation marker");
        truncMarker!.TruncationMarkerType.Should().Be(BoundedStreamCapture.StderrTruncationMarker);

        long captured = segments
            .Where(s => !s.IsEndMarker && !s.IsTruncationMarker)
            .Sum(s => (long)Encoding.UTF8.GetByteCount(s.Data));
        captured.Should().BeLessThanOrEqualTo(1_048_576,
            "captured stderr bytes must not exceed 1 MB");

        segments.Last().IsEndMarker.Should().BeTrue();
    }

    // ── AC-5: individual chunk ≤ 64 KB ────────────────────────────────────────

    [Fact]
    public async Task ReadStdoutAsync_200KBContent_EachSegmentIsAtMost64KB()
    {
        // 200 KB of ASCII so the 4 KB read buffer will produce multiple reads,
        // and EmitChunkedAsync must further split each read into ≤ 64 KB chunks.
        var content = GenerateRepeating('A', 200_000);

        var limits = new StreamCaptureLimits(MaxChunkBytes: 65_536);
        await using var capture = MakeCapture(content, string.Empty, limits);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var segments = await DrainAsync(capture.ReadStdoutAsync(cts.Token), cts.Token);

        foreach (var seg in segments.Where(s => !s.IsEndMarker && !s.IsTruncationMarker))
        {
            var byteLen = Encoding.UTF8.GetByteCount(seg.Data);
            byteLen.Should().BeLessThanOrEqualTo(65_536,
                $"segment seq={seg.SequenceNumber} encoded size {byteLen} exceeds 64 KB");
        }

        // Verify the full text is preserved (no data loss from chunking).
        var fullText = string.Concat(
            segments
                .Where(s => !s.IsEndMarker && !s.IsTruncationMarker)
                .Select(s => s.Data));
        fullText.Should().Be(content);
    }

    // ── AC-6: CRLF → LF normalisation ────────────────────────────────────────

    [Fact]
    public async Task ReadStdoutAsync_CRLFInput_OutputContainsOnlyLF()
    {
        const string crlfContent = "line1\r\nline2\r\nline3\r\n";

        await using var capture = MakeCapture(crlfContent, string.Empty);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var segments = await DrainAsync(capture.ReadStdoutAsync(cts.Token), cts.Token);
        var fullText = string.Concat(
            segments
                .Where(s => !s.IsEndMarker && !s.IsTruncationMarker)
                .Select(s => s.Data));

        fullText.Should().NotContain("\r", "all carriage returns must be stripped");
        fullText.Should().Be("line1\nline2\nline3\n");
    }

    [Fact]
    public async Task ReadStdoutAsync_BareCarriageReturn_NormalisedToLF()
    {
        const string bareCrContent = "line1\rline2\r";

        await using var capture = MakeCapture(bareCrContent, string.Empty);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var segments = await DrainAsync(capture.ReadStdoutAsync(cts.Token), cts.Token);
        var fullText = string.Concat(
            segments
                .Where(s => !s.IsEndMarker && !s.IsTruncationMarker)
                .Select(s => s.Data));

        fullText.Should().NotContain("\r");
        fullText.Should().Be("line1\nline2\n");
    }

    [Fact]
    public async Task ReadStderrAsync_CRLFInput_OutputContainsOnlyLF()
    {
        const string crlfErr = "err1\r\nerr2\r\n";

        await using var capture = MakeCapture(string.Empty, crlfErr);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var segments = await DrainAsync(capture.ReadStderrAsync(cts.Token), cts.Token);
        var fullText = string.Concat(
            segments
                .Where(s => !s.IsEndMarker && !s.IsTruncationMarker)
                .Select(s => s.Data));

        fullText.Should().NotContain("\r");
        fullText.Should().Be("err1\nerr2\n");
    }

    // ── AC-7: exit code independent of stdout ─────────────────────────────────

    [Fact]
    public async Task GetExitCodeAsync_ReturnsExpectedCode_BeforeStdoutIsRead()
    {
        // Exit code is available from the TCS immediately (simulated instant exit).
        await using var capture = MakeCapture("lots of output", string.Empty, exitCode: 42);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Do NOT read stdout before asking for the exit code.
        var exitCode = await capture.GetExitCodeAsync(cts.Token);

        exitCode.Should().Be(42);
    }

    [Fact]
    public async Task GetExitCodeAsync_WithLargeStdout_StillCompletesPromptly()
    {
        var largeStdout = GenerateRepeating('X', 500_000);
        await using var capture = MakeCapture(largeStdout, string.Empty, exitCode: 0);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Exit code must resolve even when no one reads stdout yet.
        var exitCode = await capture.GetExitCodeAsync(cts.Token);
        exitCode.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(-1)]
    public async Task GetExitCodeAsync_ReturnsExactExitCode(int expected)
    {
        await using var capture = MakeCapture(string.Empty, string.Empty, exitCode: expected);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var actual = await capture.GetExitCodeAsync(cts.Token);

        actual.Should().Be(expected);
    }

    // ── AC-8: Unicode / non-ASCII round-trip ─────────────────────────────────

    [Fact]
    public async Task ReadStdoutAsync_NonAsciiContent_RoundTripsCorrectly()
    {
        // Japanese, emoji, accented characters — all multi-byte in UTF-8.
        const string unicodeContent = "日本語テスト\nHéllo wörld\n\U0001F600\n";

        await using var capture = MakeCapture(unicodeContent, string.Empty,
            encoding: Encoding.UTF8);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var segments = await DrainAsync(capture.ReadStdoutAsync(cts.Token), cts.Token);
        var fullText = string.Concat(
            segments
                .Where(s => !s.IsEndMarker && !s.IsTruncationMarker)
                .Select(s => s.Data));

        fullText.Should().Be(unicodeContent);
    }

    // ── Sequence numbers are monotonically increasing ─────────────────────────

    [Fact]
    public async Task ReadStdoutAsync_SequenceNumbers_AreMonotonicallyIncreasing()
    {
        var content = GenerateRepeating('S', 200_000); // multiple chunks
        await using var capture = MakeCapture(content, string.Empty);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var segments = await DrainAsync(capture.ReadStdoutAsync(cts.Token), cts.Token);

        for (int i = 1; i < segments.Count; i++)
        {
            segments[i].SequenceNumber.Should().BeGreaterThan(
                segments[i - 1].SequenceNumber,
                $"segment at index {i} must have a higher sequence number than index {i-1}");
        }
    }

    // ── Truncation marker appears before end-marker ───────────────────────────

    [Fact]
    public async Task ReadStdoutAsync_WhenTruncated_TruncationMarkerPrecedesEndMarker()
    {
        var largeContent = GenerateRepeating('X', 11_000_000);
        await using var capture = MakeCapture(largeContent, string.Empty);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var segments = await DrainAsync(capture.ReadStdoutAsync(cts.Token), cts.Token);

        int truncIdx = segments.FindIndex(s => s.IsTruncationMarker);
        int endIdx   = segments.FindIndex(s => s.IsEndMarker);

        truncIdx.Should().BeGreaterThanOrEqualTo(0, "truncation marker must be present");
        endIdx.Should().BeGreaterThan(truncIdx, "end marker must follow truncation marker");
    }

    // ── No data after truncation marker (except end marker) ───────────────────

    [Fact]
    public async Task ReadStdoutAsync_WhenTruncated_NoDataSegmentsAfterTruncationMarker()
    {
        var largeContent = GenerateRepeating('X', 11_000_000);
        await using var capture = MakeCapture(largeContent, string.Empty);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var segments = await DrainAsync(capture.ReadStdoutAsync(cts.Token), cts.Token);

        int truncIdx = segments.FindIndex(s => s.IsTruncationMarker);
        var afterTrunc = segments[(truncIdx + 1)..];

        afterTrunc.Should().OnlyContain(s => s.IsEndMarker,
            "only the end-marker segment is allowed after a truncation marker");
    }
}
