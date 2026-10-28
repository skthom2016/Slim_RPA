using System.Diagnostics;
using System.Text.RegularExpressions;
using SysProcess = System.Diagnostics.Process;
using Microsoft.Extensions.Logging;
using Tracto.TerminalRuntime.Application.Shells;
using Tracto.TerminalRuntime.Domain.Shells;

namespace Tracto.TerminalRuntime.Infrastructure.Shells;

/// <summary>
/// Windows-specific implementation of <see cref="IShellDetector"/>.
/// Launches minimal sub-processes to read version strings; never executes user-supplied input.
/// Secrets must not appear in any logged or returned string — version strings are safe metadata.
/// </summary>
public sealed partial class WindowsShellDetector(ILogger<WindowsShellDetector> logger) : IShellDetector
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    // ── IShellDetector ───────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ShellInfo>> DetectAsync(CancellationToken cancellationToken = default)
    {
        var tasks = new[]
        {
            DetectCmdAsync(cancellationToken),
            DetectPowerShellAsync(cancellationToken),
            DetectWslBashAsync(cancellationToken)
        };

        var results = await Task.WhenAll(tasks);
        return results;
    }

    // ── Private probes ───────────────────────────────────────────────────────

    private async Task<ShellInfo> DetectCmdAsync(CancellationToken cancellationToken)
    {
        const string exe = "cmd.exe";
        const string args = "/c ver";

        try
        {
            var (exitCode, stdout, _) = await RunProcessAsync(exe, args, cancellationToken);
            if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
                return ShellInfo.Unavailable(ShellType.Cmd);

            // "Microsoft Windows [Version 10.0.26100.xxxx]"
            var version = ExtractBracketVersion(stdout) ?? stdout.Trim();
            return new ShellInfo(ShellType.Cmd, exe, version, IsAvailable: true);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "CMD probe failed");
            return ShellInfo.Unavailable(ShellType.Cmd);
        }
    }

    private async Task<ShellInfo> DetectPowerShellAsync(CancellationToken cancellationToken)
    {
        // Prefer pwsh (PowerShell 7+) over Windows PowerShell 5.x
        foreach (var (candidate, displayName) in new[]
        {
            ("pwsh.exe",        "pwsh"),
            ("powershell.exe",  "powershell")
        })
        {
            try
            {
                var (exitCode, stdout, _) = await RunProcessAsync(
                    candidate,
                    "-NoProfile -NonInteractive -Command \"$PSVersionTable.PSVersion.ToString()\"",
                    cancellationToken);

                if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
                {
                    var version = stdout.Trim();
                    return new ShellInfo(ShellType.PowerShell, candidate, version, IsAvailable: true);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "PowerShell probe failed for {Candidate}", displayName);
            }
        }

        return ShellInfo.Unavailable(ShellType.PowerShell);
    }

    private async Task<ShellInfo> DetectWslBashAsync(CancellationToken cancellationToken)
    {
        const string exe = "wsl.exe";
        const string args = "--version";

        try
        {
            var (exitCode, stdout, stderr) = await RunProcessAsync(exe, args, cancellationToken);

            // wsl --version exits 0 when WSL is present; older installs may need fallback
            var combined = (stdout + " " + stderr).Trim();
            if (exitCode != 0 || string.IsNullOrWhiteSpace(combined))
                return ShellInfo.Unavailable(ShellType.WslBash);

            // Parse "WSL version: 2.x.x.x" or similar
            var version = ExtractWslVersion(combined) ?? combined.Split('\n')[0].Trim();
            return new ShellInfo(ShellType.WslBash, exe, version, IsAvailable: true);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "WSL Bash probe failed");
            return ShellInfo.Unavailable(ShellType.WslBash);
        }
    }

    // ── Process helpers ──────────────────────────────────────────────────────

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(
        string fileName, string arguments, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(ProbeTimeout);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new SysProcess { StartInfo = psi };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

        await process.WaitForExitAsync(cts.Token);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return (process.ExitCode, stdout, stderr);
    }

    // ── Version parsing ──────────────────────────────────────────────────────

    [GeneratedRegex(@"\[Version\s+([^\]]+)\]", RegexOptions.IgnoreCase)]
    private static partial Regex BracketVersionRegex();

    [GeneratedRegex(@"WSL\s+[Vv]ersion\s*:\s*(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex WslVersionRegex();

    private static string? ExtractBracketVersion(string output)
    {
        var m = BracketVersionRegex().Match(output);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static string? ExtractWslVersion(string output)
    {
        var m = WslVersionRegex().Match(output);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }
}
