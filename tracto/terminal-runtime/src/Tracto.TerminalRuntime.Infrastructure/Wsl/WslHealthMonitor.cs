using Microsoft.Extensions.Logging;
using Tracto.TerminalRuntime.Application.Wsl;

namespace Tracto.TerminalRuntime.Infrastructure.Wsl;

/// <summary>
/// Production <see cref="IWslHealthMonitor"/> that queries WSL by running
/// <c>wsl --list --running</c> in a subprocess.
///
/// Polls wsl.exe directly; no plaintext secrets are passed to or captured from
/// the process (CEC invariant #4).
/// </summary>
public sealed class WslHealthMonitor(ILogger<WslHealthMonitor> logger) : IWslHealthMonitor
{
    private const string WslExecutable = "wsl";
    private const string WslArgs       = "--list --running";

    private static readonly char[] LineSeparators = ['\r', '\n'];

    /// <inheritdoc/>
    public async Task<bool> IsDistroRunningAsync(
        string distroName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(distroName))
            return false;

        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName               = WslExecutable,
                Arguments              = WslArgs,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                // Force UTF-16 LE so we can strip NUL bytes from the Windows
                // console codepage header that wsl.exe emits.
                StandardOutputEncoding = System.Text.Encoding.Unicode
            };

            using var process = new System.Diagnostics.Process { StartInfo = startInfo };
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            // wsl --list --running may emit a header line and trailing CRLFs.
            // Each distro name can carry trailing NUL characters on some builds.
            return output
                .Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Any(line => string.Equals(
                    line.Trim().TrimEnd('\0'),
                    distroName.Trim(),
                    StringComparison.OrdinalIgnoreCase));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // If wsl.exe is not present or fails, treat the distro as not running.
            logger.LogWarning(ex,
                "Failed to query WSL running distros for '{DistroName}'; treating as not running.",
                distroName);
            return false;
        }
    }
}
