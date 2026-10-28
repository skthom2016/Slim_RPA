using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.Execution;
using Tracto.TerminalRuntime.Application.Redaction;
using Tracto.TerminalRuntime.Domain.Arguments;
using Tracto.TerminalRuntime.Domain.Redaction;
using Tracto.TerminalRuntime.Infrastructure.Arguments;

namespace Tracto.TerminalRuntime.Infrastructure.Execution;

/// <summary>
/// Concrete <see cref="ITerminalProcessRunner"/> that launches a validated process,
/// captures its stdout and stderr concurrently to prevent deadlock, then immediately
/// routes all captured text through <see cref="IOutputRedactor"/> before returning it.
///
/// SECURITY CONTRACT (CEC invariant #4):
/// Raw process output is held in local variables only long enough to be redacted.
/// Only the redacted <see cref="Application.Execution.ProcessRunResult"/> leaves this method.
/// Callers MUST use <see cref="ProcessRunResult.StdoutContent"/> and
/// <see cref="ProcessRunResult.StderrContent"/> (never a cached copy of the raw strings)
/// when writing to <see cref="Application.IRunJournal"/>, evidence artifacts, or Orchestrator payloads.
/// </summary>
public sealed class TerminalProcessRunner : ITerminalProcessRunner
{
    private readonly ProcessStartSpecBuilder      _builder;
    private readonly IOutputRedactor              _redactor;
    private readonly ILogger<TerminalProcessRunner> _logger;

    public TerminalProcessRunner(
        ProcessStartSpecBuilder        builder,
        IOutputRedactor                redactor,
        ILogger<TerminalProcessRunner> logger)
    {
        _builder  = builder;
        _redactor = redactor;
        _logger   = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<ProcessRunResult, Error>> RunAsync(
        ProcessStartSpec      spec,
        OutputRedactionPolicy policy,
        CancellationToken     ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(policy);

        // Validate the policy before starting the process so we fail fast.
        var policyCheck = _redactor.ValidatePolicy(policy);
        if (policyCheck.IsFailure)
            return Result<ProcessRunResult, Error>.Fail(policyCheck.Error);

        ProcessStartInfo psi;
        try
        {
            psi = _builder.Build(spec);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            _logger.LogError(ex, "ProcessStartSpecBuilder rejected the spec.");
            return Result<ProcessRunResult, Error>.Fail(
                Error.General("ProcessRunner.BuildFailed", ex.Message));
        }

        using var process = new System.Diagnostics.Process { StartInfo = psi, EnableRaisingEvents = true };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start process '{FileName}'.", psi.FileName);
            return Result<ProcessRunResult, Error>.Fail(
                Error.General("ProcessRunner.StartFailed",
                    $"Process could not be started: {ex.Message}"));
        }

        // Capture stdout and stderr concurrently.  Reading them sequentially risks
        // deadlock when the child process fills one pipe while blocked writing to the other.
        // Raw output is kept in local variables and NEVER passed to any external caller.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        string rawStdout;
        string rawStderr;
        try
        {
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            rawStdout = stdoutTask.Result;
            rawStderr = stderrTask.Result;
        }
        catch (OperationCanceledException)
        {
            // Best-effort: kill the child if cancellation was requested.
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading process streams.");
            return Result<ProcessRunResult, Error>.Fail(
                Error.General("ProcessRunner.StreamReadFailed",
                    $"Failed to read process output streams: {ex.Message}"));
        }

        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var exitCode = process.ExitCode;

        // REDACT stdout immediately — before touching any downstream path.
        var stdoutResult = _redactor.Redact(rawStdout, policy);
        if (stdoutResult.IsFailure)
        {
            _logger.LogError("stdout redaction failed: {Code}", stdoutResult.Error.Code);
            return Result<ProcessRunResult, Error>.Fail(stdoutResult.Error);
        }

        // REDACT stderr immediately.
        var stderrResult = _redactor.Redact(rawStderr, policy);
        if (stderrResult.IsFailure)
        {
            _logger.LogError("stderr redaction failed: {Code}", stderrResult.Error.Code);
            return Result<ProcessRunResult, Error>.Fail(stderrResult.Error);
        }

        var wasRedacted = stdoutResult.Value.WasRedacted || stderrResult.Value.WasRedacted;

        if (wasRedacted)
            _logger.LogInformation(
                "Output redaction applied for process '{FileName}' (exit {ExitCode}).",
                psi.FileName, exitCode);

        return Result<ProcessRunResult, Error>.Ok(new ProcessRunResult(
            StdoutContent: stdoutResult.Value.Content,
            StderrContent: stderrResult.Value.Content,
            ExitCode:      exitCode,
            WasRedacted:   wasRedacted));
    }
}
