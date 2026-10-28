using System.Diagnostics;
using System.Runtime.Versioning;
using SysProcess = System.Diagnostics.Process;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.Identity;
using Tracto.TerminalRuntime.Domain.Identity;

namespace Tracto.TerminalRuntime.Infrastructure.Identity;

/// <summary>
/// Infrastructure implementation of <see cref="IIdentityVerifier"/>.
/// Uses <see cref="WindowsIdentity.GetCurrent()"/> for Windows checks and
/// <c>wsl -d {distro} --exec whoami</c> for WSL checks.
/// Process launching and OS calls live exclusively here — never in Application or Domain.
/// </summary>
public sealed class WindowsIdentityVerifier(ILogger<WindowsIdentityVerifier> logger) : IIdentityVerifier
{
    // ── Windows identity ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    [SupportedOSPlatform("windows")]
    public Task<Result<IdentityVerificationResult, Error>> VerifyWindowsIdentityAsync(
        string expectedIdentity,
        CancellationToken ct = default)
    {
        string currentIdentity;
        try
        {
            currentIdentity = WindowsIdentity.GetCurrent().Name;
        }
        catch (Exception ex)
        {
            // Log the exception type only — never the identity value (no secrets in logs)
            logger.LogWarning(
                "Failed to obtain current Windows identity. ExceptionType={ExceptionType}",
                ex.GetType().Name);

            return Task.FromResult(
                Result.Fail<IdentityVerificationResult>(
                    Error.General(
                        "IDENTITY_500",
                        "Unable to resolve the current Windows identity.",
                        detailCode: "terminal.identityResolutionFailed")));
        }

        if (!string.Equals(currentIdentity, expectedIdentity, StringComparison.OrdinalIgnoreCase))
        {
            // AC-1: mismatch → AUTHENTICATION / terminal.identityMismatch
            // Do NOT log the actual or expected identity values (AC-1 invariant, CEC §4)
            logger.LogWarning(
                "Windows identity mismatch detected. Expected identity does not match effective identity.");

            return Task.FromResult(
                Result.Fail<IdentityVerificationResult>(
                    Error.Authentication(
                        "IDENTITY_401",
                        "The effective Windows identity does not match the policy-specified identity.",
                        detailCode: "terminal.identityMismatch")));
        }

        return Task.FromResult(
            Result.Ok(new IdentityVerificationResult(currentIdentity, IdentityVerificationKind.Windows)));
    }

    // ── WSL identity ──────────────────────────────────────────────────────────

    /// <inheritdoc/>
    [SupportedOSPlatform("windows")]
    public async Task<Result<IdentityVerificationResult, Error>> VerifyWslIdentityAsync(
        string distroName,
        string expectedUser,
        string? orchestratorDistroName,
        CancellationToken ct = default)
    {
        // AC-4: Reject if the target distro is the Orchestrator's own WSL environment
        if (!string.IsNullOrWhiteSpace(orchestratorDistroName) &&
            string.Equals(distroName, orchestratorDistroName, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "wsl-bash command targets the Orchestrator's own distro — rejected by policy (AC-4).");

            return Result.Fail<IdentityVerificationResult>(
                Error.PolicyDenied(
                    "IDENTITY_403",
                    "Commands targeting the Orchestrator's WSL distro are not permitted.",
                    detailCode: "terminal.orchestratorDistroRejected"));
        }

        // AC-3: Resolve Linux user inside the specified distro
        string? resolvedUser = await RunWslWhoamiAsync(distroName, ct);

        if (resolvedUser is null)
        {
            // Distro unavailable, not installed, or not in the approved list
            logger.LogWarning(
                "WSL distro is unavailable or unapproved. DistroName={DistroName}", distroName);

            return Result.Fail<IdentityVerificationResult>(
                Error.Environment(
                    "IDENTITY_404",
                    $"WSL distro '{distroName}' is unavailable or not approved on this host.",
                    detailCode: "terminal.wslDistroUnavailable"));
        }

        // AC-1 / AC-2: Compare resolved user with policy user
        if (!string.Equals(resolvedUser, expectedUser, StringComparison.OrdinalIgnoreCase))
        {
            // Do NOT log expected or resolved user (CEC §4)
            logger.LogWarning(
                "WSL identity mismatch detected in distro '{DistroName}'.", distroName);

            return Result.Fail<IdentityVerificationResult>(
                Error.Authentication(
                    "IDENTITY_401",
                    "The effective WSL identity does not match the policy-specified user.",
                    detailCode: "terminal.identityMismatch"));
        }

        return Result.Ok(new IdentityVerificationResult(resolvedUser, IdentityVerificationKind.Wsl));
    }

    // ── Subprocess helper ─────────────────────────────────────────────────────

    /// <summary>
    /// Runs <c>wsl -d {distroName} --exec whoami</c> and returns the trimmed stdout,
    /// or <c>null</c> when the distro is unavailable or the process fails.
    /// </summary>
    private static async Task<string?> RunWslWhoamiAsync(string distroName, CancellationToken ct)
    {
        try
        {
            using var process = new SysProcess();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "wsl",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Use ArgumentList (not Arguments string) to avoid shell injection
            process.StartInfo.ArgumentList.Add("-d");
            process.StartInfo.ArgumentList.Add(distroName);
            process.StartInfo.ArgumentList.Add("--exec");
            process.StartInfo.ArgumentList.Add("whoami");

            process.Start();

            // Read output concurrently with process exit to prevent deadlock on full pipe buffers
            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);

            // Drain both streams
            var stdout = await stdoutTask;
            await stderrTask;

            return process.ExitCode == 0
                ? stdout.Trim()
                : null;
        }
        catch (OperationCanceledException)
        {
            throw; // let CancellationToken propagate normally
        }
        catch
        {
            return null;
        }
    }
}
