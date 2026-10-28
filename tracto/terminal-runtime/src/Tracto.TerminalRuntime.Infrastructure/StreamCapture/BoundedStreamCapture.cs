using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Tracto.TerminalRuntime.Application.StreamCapture;
using Tracto.TerminalRuntime.Domain.StreamCapture;

namespace Tracto.TerminalRuntime.Infrastructure.StreamCapture;

/// <summary>
/// Deadlock-safe, bounded capture of process stdout and stderr.
///
/// Deadlock prevention (AC-2):
///   A background Task is started for EACH stream immediately at construction.
///   Both tasks drain their respective pipe concurrently via Channel&lt;T&gt;, so a
///   process that floods stderr while its stdout buffer is also full can never
///   block the owning thread waiting for one stream to drain before reading the other.
///
/// Truncation (AC-3 / AC-4):
///   When the running byte total exceeds MaxStdoutBytes (10 MB) or MaxStderrBytes (1 MB),
///   the remaining data is discarded and a terminal.outputTruncated /
///   terminal.stderrTruncated marker segment is emitted.  The run continues normally.
///
/// Chunk splitting (AC-5):
///   Each emitted segment's Data, when encoded with the declared encoding,
///   does not exceed MaxChunkBytes (64 KB).
///
/// LF normalisation (AC-6):
///   CRLF and bare CR sequences are replaced with LF before the text is stored.
///
/// Exit code independence (AC-7):
///   The exit code is captured via a dedicated task that awaits process termination;
///   it is not gated on any stdout/stderr read completing.
///
/// Unicode / encoding (AC-8):
///   The caller supplies the encoding that matches the process's declared console encoding.
///   TextReader handles multi-byte character boundaries, so non-ASCII output round-trips.
/// </summary>
public sealed class BoundedStreamCapture : IStreamCapture
{
    // ── Truncation marker strings (AC-3 / AC-4) ──────────────────────────────

    public const string StdoutTruncationMarker = "terminal.outputTruncated";
    public const string StderrTruncationMarker = "terminal.stderrTruncated";

    // ── Internal state ───────────────────────────────────────────────────────

    private readonly Channel<StreamSegment> _stdoutChannel;
    private readonly Channel<StreamSegment> _stderrChannel;
    private readonly TaskCompletionSource<int> _exitCodeTcs;
    private readonly Task _stdoutTask;
    private readonly Task _stderrTask;
    private bool _disposed;

    // ── Construction ─────────────────────────────────────────────────────────

    /// <summary>
    /// Starts background capture immediately.
    /// </summary>
    /// <param name="stdoutReader">
    ///   TextReader wrapping the process stdout pipe, using the declared encoding.
    ///   Ownership is transferred: the reader will be consumed by the background task.
    /// </param>
    /// <param name="stderrReader">
    ///   TextReader wrapping the process stderr pipe.
    /// </param>
    /// <param name="waitForExitAsync">
    ///   Async factory that resolves to the process exit code once the process terminates.
    ///   Called on a thread-pool thread; must not throw for cancellation — use the token.
    /// </param>
    /// <param name="limits">Byte and chunk limits to enforce.</param>
    /// <param name="encoding">
    ///   The encoding used to convert text to bytes for limit accounting.
    ///   Defaults to UTF-8 when null.
    /// </param>
    public BoundedStreamCapture(
        TextReader stdoutReader,
        TextReader stderrReader,
        Func<CancellationToken, Task<int>> waitForExitAsync,
        StreamCaptureLimits limits,
        Encoding? encoding = null)
    {
        ArgumentNullException.ThrowIfNull(stdoutReader);
        ArgumentNullException.ThrowIfNull(stderrReader);
        ArgumentNullException.ThrowIfNull(waitForExitAsync);
        ArgumentNullException.ThrowIfNull(limits);

        var enc = encoding ?? Encoding.UTF8;

        _stdoutChannel = Channel.CreateUnbounded<StreamSegment>(
            new UnboundedChannelOptions
            {
                SingleWriter = true,
                AllowSynchronousContinuations = false
            });

        _stderrChannel = Channel.CreateUnbounded<StreamSegment>(
            new UnboundedChannelOptions
            {
                SingleWriter = true,
                AllowSynchronousContinuations = false
            });

        _exitCodeTcs = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // Both capture tasks start NOW.  Using Task.Run ensures they are scheduled
        // on the thread pool immediately, before any consumer calls ReadStdoutAsync.
        // This is the core deadlock-prevention invariant (AC-2).
        _stdoutTask = Task.Run(() => CaptureAsync(
            stdoutReader,
            _stdoutChannel.Writer,
            label: "stdout",
            maxTotalBytes: limits.MaxStdoutBytes,
            maxChunkBytes: limits.MaxChunkBytes,
            truncationMarkerType: StdoutTruncationMarker,
            encoding: enc,
            ct: CancellationToken.None));

        _stderrTask = Task.Run(() => CaptureAsync(
            stderrReader,
            _stderrChannel.Writer,
            label: "stderr",
            maxTotalBytes: limits.MaxStderrBytes,
            maxChunkBytes: limits.MaxChunkBytes,
            truncationMarkerType: StderrTruncationMarker,
            encoding: enc,
            ct: CancellationToken.None));

        // Exit code task is independent of stream consumption (AC-7).
        _ = Task.Run(async () =>
        {
            try
            {
                var code = await waitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                _exitCodeTcs.TrySetResult(code);
            }
            catch (OperationCanceledException)
            {
                _exitCodeTcs.TrySetCanceled();
            }
            catch (Exception ex)
            {
                _exitCodeTcs.TrySetException(ex);
            }
        });
    }

    // ── Static factory (convenience for real Process objects) ────────────────

    /// <summary>
    /// Creates a BoundedStreamCapture from a started <see cref="System.Diagnostics.Process"/>.
    /// The process must have been started with RedirectStandardOutput and RedirectStandardError = true.
    /// </summary>
    public static BoundedStreamCapture FromProcess(
        System.Diagnostics.Process process,
        StreamCaptureLimits? limits = null,
        Encoding? encoding = null)
    {
        ArgumentNullException.ThrowIfNull(process);
        var enc = encoding ?? Encoding.UTF8;
        return new BoundedStreamCapture(
            new StreamReader(process.StandardOutput.BaseStream, enc),
            new StreamReader(process.StandardError.BaseStream, enc),
            async ct => { await process.WaitForExitAsync(ct).ConfigureAwait(false); return process.ExitCode; },
            limits ?? StreamCaptureLimits.Default,
            enc);
    }

    // ── IStreamCapture ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async IAsyncEnumerable<StreamSegment> ReadStdoutAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var segment in _stdoutChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return segment;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<StreamSegment> ReadStderrAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var segment in _stderrChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return segment;
    }

    /// <inheritdoc/>
    public Task<int> GetExitCodeAsync(CancellationToken ct = default) =>
        _exitCodeTcs.Task.WaitAsync(ct);

    // ── IAsyncDisposable ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Await capture completion so callers get all data before the instance is torn down.
        await _stdoutTask.ConfigureAwait(false);
        await _stderrTask.ConfigureAwait(false);
    }

    // ── Core capture logic ───────────────────────────────────────────────────

    /// <summary>
    /// Reads <paramref name="reader"/> to EOF (or cancellation), emitting segments
    /// into <paramref name="writer"/>.  All limit and normalisation invariants are
    /// enforced here.
    /// </summary>
    private static async Task CaptureAsync(
        TextReader reader,
        ChannelWriter<StreamSegment> writer,
        string label,
        long maxTotalBytes,
        int maxChunkBytes,
        string truncationMarkerType,
        Encoding encoding,
        CancellationToken ct)
    {
        long totalBytes = 0;
        int seq = 0;
        bool truncated = false;

        // 4096-char read buffer.  Sized to produce chunks well under MaxChunkBytes
        // for typical ASCII/Latin output; non-ASCII heavy streams will be further
        // split by EmitChunkedAsync.
        var charBuffer = new char[4096];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                int charsRead = await reader.ReadAsync(charBuffer.AsMemory(), ct)
                    .ConfigureAwait(false);

                if (charsRead == 0) break; // natural EOF

                if (truncated)
                {
                    // Drain the stream so the process is not blocked by a full pipe buffer,
                    // but discard the content.
                    continue;
                }

                // AC-6: normalise line endings before any byte accounting or storage.
                var text = NormalizeLF(new string(charBuffer, 0, charsRead));

                long byteLen = encoding.GetByteCount(text);

                if (totalBytes + byteLen > maxTotalBytes)
                {
                    // Accept as much as fits within the limit.
                    long allowedBytes = maxTotalBytes - totalBytes;
                    if (allowedBytes > 0)
                    {
                        var partial = TruncateToByteLength(text, (int)allowedBytes, encoding);
                        if (partial.Length > 0)
                        {
                            seq = await EmitChunkedAsync(
                                writer, label, seq, partial, maxChunkBytes, encoding, ct)
                                .ConfigureAwait(false);
                        }
                    }

                    // AC-3 / AC-4: emit truncation marker.
                    await writer.WriteAsync(
                        StreamSegment.TruncatedAt(label, seq++, truncationMarkerType), ct)
                        .ConfigureAwait(false);

                    truncated = true;
                    continue;
                }

                totalBytes += byteLen;
                seq = await EmitChunkedAsync(
                    writer, label, seq, text, maxChunkBytes, encoding, ct)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on consumer cancellation; fall through to finally.
        }
        catch (Exception)
        {
            // Stream read errors (broken pipe, process crash) must not propagate —
            // the run should continue with what was captured so far.
        }
        finally
        {
            // AC-1: emit stream-end marker so consumers know the stream is exhausted.
            writer.TryWrite(StreamSegment.EndOf(label, seq));
            writer.TryComplete();
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Emits <paramref name="text"/> as one or more segments, each with an encoded
    /// byte size not exceeding <paramref name="maxChunkBytes"/> (AC-5).
    /// Returns the next available sequence number.
    /// </summary>
    private static async Task<int> EmitChunkedAsync(
        ChannelWriter<StreamSegment> writer,
        string label,
        int startSeq,
        string text,
        int maxChunkBytes,
        Encoding encoding,
        CancellationToken ct)
    {
        int seq = startSeq;
        int offset = 0;

        while (offset < text.Length)
        {
            int charCount = FindCharCountForByteLimit(text, offset, maxChunkBytes, encoding);
            if (charCount == 0) break; // defensive guard against degenerate input

            var chunk = text.Substring(offset, charCount);
            await writer.WriteAsync(new StreamSegment(label, seq++, chunk), ct)
                .ConfigureAwait(false);

            offset += charCount;
        }

        return seq;
    }

    /// <summary>
    /// Returns how many chars starting at <paramref name="offset"/> can be taken
    /// from <paramref name="text"/> such that the encoded byte length ≤ <paramref name="maxBytes"/>.
    /// Uses binary search; O(log n) calls to <see cref="Encoding.GetByteCount"/>.
    /// </summary>
    private static int FindCharCountForByteLimit(
        string text, int offset, int maxBytes, Encoding encoding)
    {
        int available = text.Length - offset;
        if (available <= 0) return 0;

        // Fast path: all ASCII → 1 byte per char.
        int totalNeeded = encoding.GetByteCount(text.AsSpan(offset, available));
        if (totalNeeded <= maxBytes) return available;

        // Binary search for the largest char count that fits within maxBytes.
        int lo = 1, hi = available;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (encoding.GetByteCount(text.AsSpan(offset, mid)) <= maxBytes)
                lo = mid;
            else
                hi = mid - 1;
        }

        return lo;
    }

    /// <summary>
    /// Truncates <paramref name="text"/> so that its encoded form does not exceed
    /// <paramref name="maxBytes"/> bytes.  Ensures the result is valid at a character boundary.
    /// </summary>
    private static string TruncateToByteLength(string text, int maxBytes, Encoding encoding)
    {
        if (maxBytes <= 0) return string.Empty;
        int charCount = FindCharCountForByteLimit(text, 0, maxBytes, encoding);
        return charCount >= text.Length ? text : text[..charCount];
    }

    /// <summary>
    /// Normalises all line-ending variants to LF (AC-6).
    /// Handles CRLF first to avoid double-replacement.
    /// </summary>
    private static string NormalizeLF(string text)
    {
        // Replace CRLF before bare CR so we don't turn CRLF into double-LF.
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        text = text.Replace("\r",   "\n", StringComparison.Ordinal);
        return text;
    }
}
