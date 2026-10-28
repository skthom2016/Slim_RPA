using System.Text;
using Microsoft.Extensions.Logging;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.Stdin;
using Tracto.TerminalRuntime.Domain.Stdin;

namespace Tracto.TerminalRuntime.Infrastructure.Stdin;

/// <summary>
/// Writes a bounded, pre-approved string to a process standard-input pipe.
///
/// Security invariants (TER-US-012):
/// <list type="bullet">
///   <item>The written value is NEVER emitted to any log message, structured log property,
///         evidence field or exception (CEC invariant #4).</item>
///   <item>Payloads exceeding <see cref="StdinPolicy.MaxBytes"/> bytes (UTF-8) are rejected
///         before any I/O is performed (AC-5).</item>
///   <item>A closed or exited process pipe returns
///         errorCategory=ENVIRONMENT / detailCode=terminal.processExited (AC-1).</item>
/// </list>
/// </summary>
public sealed class StdinWriter : IStdinWriter
{
    private const string SizeExceededDetailCode = "terminal.stdinSizeExceeded";
    private const string ProcessExitedDetailCode = "terminal.processExited";

    private readonly ILogger<StdinWriter> _logger;

    public StdinWriter(ILogger<StdinWriter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<Result<bool, Error>> WriteAsync(
        Stream stdinStream,
        string value,
        StdinPolicy policy,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stdinStream);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(policy);

        // AC-5: Reject oversized payloads BEFORE any write is attempted.
        // We measure UTF-8 byte count, which is the encoding used for the write.
        int byteCount = Encoding.UTF8.GetByteCount(value);
        if (byteCount > policy.MaxBytes)
        {
            _logger.LogWarning(
                "Stdin payload of {ByteCount} bytes exceeds the policy limit of {MaxBytes} bytes; write rejected.",
                byteCount,
                policy.MaxBytes);

            return Result<bool, Error>.Fail(
                Error.Validation(
                    "STDIN_POLICY_001",
                    $"Stdin payload size ({byteCount} bytes) exceeds the maximum allowed size of {policy.MaxBytes} bytes.",
                    SizeExceededDetailCode));
        }

        // CEC invariant #4: log only byte-count metadata, never the value itself.
        _logger.LogDebug(
            "Writing stdin payload ({ByteCount} bytes) to process stdin pipe.",
            byteCount);

        try
        {
            // Append a newline so interactive programs receive a complete line.
            byte[] encoded = Encoding.UTF8.GetBytes(value + "\n");
            await stdinStream.WriteAsync(encoded, ct);
            await stdinStream.FlushAsync(ct);

            return Result<bool, Error>.Ok(true);
        }
        catch (OperationCanceledException)
        {
            // Propagate external cancellation — do not swallow.
            throw;
        }
        catch (Exception ex) when (
            ex is IOException
            or ObjectDisposedException
            or InvalidOperationException)
        {
            // AC-1: the process has already exited and its stdin pipe is closed.
            // The exception message might contain system details; we do NOT forward it
            // to Orchestrator payloads, only log a safe summary.
            _logger.LogWarning(
                "Stdin write failed: process stdin pipe appears closed (process likely exited). Exception type: {ExType}.",
                ex.GetType().Name);

            return Result<bool, Error>.Fail(
                Error.Environment(
                    "STDIN_POLICY_002",
                    "Cannot write to process stdin: the process has exited or the stdin pipe is closed.",
                    ProcessExitedDetailCode));
        }
    }
}
