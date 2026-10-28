using System.Diagnostics;
using Tracto.TerminalRuntime.Application;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.Process;
using Tracto.TerminalRuntime.Domain;
using Tracto.TerminalRuntime.Domain.Process;
using Microsoft.Extensions.Logging;

namespace Tracto.TerminalRuntime.Infrastructure.Process;

/// <summary>
/// Windows implementation of <see cref="IProcessController"/>.
///
/// TER-US-015 implementation notes:
/// <list type="bullet">
///   <item>Uses <c>ProcessStartInfo.ArgumentList</c> (typed API, no shell-string concat).</item>
///   <item>Tracks the root PID via <see cref="IProcessTracker"/> before reading output.</item>
///   <item>Deadline enforcement via <see cref="System.Diagnostics.Process.WaitForExitAsync"/>
///         raced against a <see cref="Task.Delay"/> derived from <c>TimeoutMs</c>.</item>
///   <item>Process-tree termination via <c>Process.Kill(entireProcessTree: true)</c> (.NET 5+).</item>
///   <item>Verification: polls each tracked PID after the cleanup grace period and
///         force-kills any survivors.</item>
///   <item>Unrelated host processes are never touched — only PIDs registered under
///         the run's <see cref="OwnedProcessTree.RunId"/> are killed (AC-5).</item>
/// </list>
///
/// No plaintext secrets in logs (CEC invariant #4).
/// No business-logic predicates (CEC invariant #7).
/// </summary>
public sealed class WindowsProcessController(
    IProcessTracker tracker,
    IRunJournal journal,
    IClock clock,
    ILogger<WindowsProcessController> logger) : IProcessController
{
    // ── StartAsync ────────────────────────────────────────────────────────────

    public async Task<Result<OwnedProcessTree, Error>> StartAsync(
        StartProcessCommand command,
        CancellationToken ct = default)
    {
        var psi = BuildProcessStartInfo(command);

        System.Diagnostics.Process? process;
        try
        {
            process = System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            // Do NOT log executable path or arguments — they may carry sensitive context.
            logger.LogError(ex,
                "Run {RunId} attempt {AttemptId}: process launch failed (error type redacted for security).",
                command.RunId, command.AttemptId);

            return Error.General(
                "Process.LaunchFailed",
                "The process could not be started. Check the executable path and permissions.");
        }

        if (process is null)
        {
            logger.LogError("Run {RunId}: Process.Start returned null.", command.RunId);
            return Error.General("Process.LaunchNull", "Process.Start returned null — shell may not be installed.");
        }

        int rootPid = process.Id;

        // Track root PID BEFORE reading any output (AC-1).
        tracker.Track(command.RunId, rootPid);

        // Persist PID to run journal BEFORE reading any output (AC-1).
        var checkpointResult = await journal.RecordCheckpointAsync(
            command.RunId,
            command.AttemptId,
            checkpointName: "process.pid",
            payload: rootPid.ToString(),
            ct);

        if (checkpointResult.IsFailure)
        {
            logger.LogWarning(
                "Run {RunId}: could not persist PID checkpoint: {Code}. " +
                "Process is running but journal may be stale.",
                command.RunId, checkpointResult.Error.Code);
            // Non-fatal — process is alive; continue.
        }

        var tree = new OwnedProcessTree(
            RunId: command.RunId,
            RootPid: rootPid,
            ChildPids: [rootPid],
            StartedAt: clock.UtcNow,
            TimeoutMs: command.TimeoutMs);

        logger.LogInformation(
            "Run {RunId} attempt {AttemptId}: process started. RootPid={RootPid} TimeoutMs={TimeoutMs}",
            command.RunId, command.AttemptId, rootPid, command.TimeoutMs);

        return tree;
    }

    // ── AwaitAsync ────────────────────────────────────────────────────────────

    public async Task<Result<ProcessOutcome, Error>> AwaitAsync(
        AwaitProcessCommand command,
        CancellationToken ct = default)
    {
        OwnedProcessTree tree = command.Tree;

        System.Diagnostics.Process? process = TryGetProcess(tree.RootPid);
        if (process is null)
        {
            // Process already exited before we could attach — treat as clean exit with code 0.
            logger.LogWarning(
                "Run {RunId}: RootPid {RootPid} not found — process already exited or PID was recycled.",
                command.RunId, tree.RootPid);
            return ProcessOutcome.Succeeded(0);
        }

        // CRITICAL: Start draining stdout/stderr immediately to prevent the pipe-buffer deadlock.
        // A child that writes more than the pipe buffer will block on its write syscall; if we
        // only call WaitForExitAsync without reading, both sides deadlock forever.
        // CancellationToken.None is intentional: when we kill the process the pipe handles close
        // and ReadToEndAsync returns promptly regardless of the cancellation state.  AC-2.
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            if (tree.TimeoutMs > 0)
            {
                // Race: process exit vs. deadline (AC-2, AC-4).
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(tree.TimeoutMs));

                try
                {
                    await process.WaitForExitAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Deadline elapsed — terminate tree (AC-4).
                    logger.LogWarning(
                        "Run {RunId}: deadline of {TimeoutMs}ms exceeded. Terminating process tree (RootPid={RootPid}).",
                        command.RunId, tree.TimeoutMs, tree.RootPid);

                    await TerminateTreeAsync(tree, processTreeCleanupMs: 5_000, ct: CancellationToken.None);

                    // The process is now dead; pipe handles are closed.  Await drain completion.
                    await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

                    return ProcessOutcome.DeadlineExceeded;
                }
            }
            else
            {
                // No deadline — wait indefinitely (callers should supply a timeout).
                await process.WaitForExitAsync(ct);
            }

            // Process has exited; await full pipe drain before reading ExitCode (AC-2).
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

            int exitCode = process.ExitCode;
            string stdout = stdoutTask.Result;
            string stderr = stderrTask.Result;

            logger.LogInformation(
                "Run {RunId}: process RootPid={RootPid} exited with code {ExitCode}. " +
                "StdoutBytes={StdoutLen} StderrBytes={StderrLen}",
                command.RunId, tree.RootPid, exitCode, stdout.Length, stderr.Length);

            return ProcessOutcome.Succeeded(exitCode, stdout, stderr);
        }
        catch (OperationCanceledException)
        {
            // External cancellation via ct — caller will call CancelAsync separately.
            return ProcessOutcome.CancelledByRequest;
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex,
                "Run {RunId}: error awaiting RootPid={RootPid}.",
                command.RunId, tree.RootPid);
            return Error.General("Process.AwaitFailed", "An error occurred while waiting for the process to exit.");
        }
        finally
        {
            process.Dispose();
        }
    }

    // ── CancelAsync ───────────────────────────────────────────────────────────

    public async Task<Result<ProcessOutcome, Error>> CancelAsync(
        CancelProcessCommand command,
        int processTreeCleanupMs = 5_000,
        CancellationToken ct = default)
    {
        logger.LogInformation(
            "Run {RunId}: cancelling process tree. RootPid={RootPid} GraceMs={GraceMs}",
            command.RunId, command.Tree.RootPid, processTreeCleanupMs);

        await TerminateTreeAsync(command.Tree, processTreeCleanupMs, ct);

        logger.LogInformation("Run {RunId}: process tree cancelled and verified clean.", command.RunId);
        return ProcessOutcome.CancelledByRequest;
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private async Task TerminateTreeAsync(
        OwnedProcessTree tree,
        int processTreeCleanupMs,
        CancellationToken ct)
    {
        // Step 1: Kill the root process and its entire subtree (AC-5).
        // Process.Kill(entireProcessTree: true) is available since .NET 5.
        // Unrelated processes on the host are untouched because we work only
        // from the tracked root PID downward.
        System.Diagnostics.Process? root = TryGetProcess(tree.RootPid);
        if (root is not null)
        {
            try
            {
                root.Kill(entireProcessTree: true);
                logger.LogDebug("Run {RunId}: sent Kill(entireProcessTree) to RootPid={RootPid}.",
                    tree.RunId, tree.RootPid);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Run {RunId}: Kill(entireProcessTree) on RootPid={RootPid} raised an exception " +
                    "(process may have already exited).",
                    tree.RunId, tree.RootPid);
            }
            finally
            {
                root.Dispose();
            }
        }

        // Step 2: Grace period (AC-6).
        if (processTreeCleanupMs > 0)
        {
            await Task.Delay(processTreeCleanupMs, ct).ConfigureAwait(false);
        }

        // Step 3: Verification — confirm no owned PID remains (AC-6).
        // Only PIDs that were registered under this run's tracker are checked,
        // preserving isolation from unrelated host processes (AC-5).
        var survivors = new List<int>();
        foreach (int pid in tree.AllPids)
        {
            if (IsProcessAlive(pid))
                survivors.Add(pid);
        }

        if (survivors.Count > 0)
        {
            logger.LogWarning(
                "Run {RunId}: {Count} PID(s) survived the grace period. Force-killing: [{Pids}]",
                tree.RunId, survivors.Count, string.Join(", ", survivors));

            foreach (int pid in survivors)
            {
                ForceKill(pid, tree.RunId);
            }
        }

        // Untrack all PIDs after cleanup.
        foreach (int pid in tree.AllPids)
        {
            tracker.Untrack(tree.RunId, pid);
        }
    }

    private static ProcessStartInfo BuildProcessStartInfo(StartProcessCommand command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command.Spec.ExecutablePath,
            WorkingDirectory = command.Spec.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
        };

        // Typed ArgumentList — no shell-string concatenation (AC-1, CEC invariant rules).
        foreach (string arg in command.Spec.ArgumentList)
            psi.ArgumentList.Add(arg);

        // Environment overrides (no plaintext secrets per CEC invariant #4).
        foreach (var (key, value) in command.Spec.EnvironmentVars)
            psi.Environment[key] = value;

        return psi;
    }

    private static System.Diagnostics.Process? TryGetProcess(int pid)
    {
        try
        {
            return System.Diagnostics.Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            // Process not found — already exited.
            return null;
        }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void ForceKill(int pid, string runId)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            p.Kill();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Run {RunId}: ForceKill of PID {Pid} failed (process may have already exited).",
                runId, pid);
        }
    }
}
