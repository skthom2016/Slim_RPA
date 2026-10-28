using Microsoft.Extensions.Logging;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Shells;

namespace Tracto.TerminalRuntime.Application.Shells;

/// <summary>
/// Application-layer service that:
///   1. Asks <see cref="IShellDetector"/> to probe the host.
///   2. Builds a <see cref="ShellCapabilityAdvertisement"/> for the Orchestrator.
///   3. Validates a requested <see cref="ShellType"/> before any command executes (AC-2, AC-3).
/// No process-launching logic lives here — that belongs in Infrastructure.
/// </summary>
public sealed class ShellValidationService(
    IShellDetector detector,
    ILogger<ShellValidationService> logger)
{
    private ShellCapabilityAdvertisement? _advertisement;

    // ── Initialisation ───────────────────────────────────────────────────────

    /// <summary>
    /// Probes all shells and caches the result.
    /// Should be called once on worker start (AC-1).
    /// </summary>
    public async Task<ShellCapabilityAdvertisement> InitialiseAsync(CancellationToken cancellationToken = default)
    {
        var shells = await detector.DetectAsync(cancellationToken);

        foreach (var shell in shells)
        {
            if (shell.IsAvailable)
                logger.LogInformation("Shell detected: {ShellType} version {Version} at {Path}",
                    shell.ShellType, shell.Version, shell.ExecutablePath);
            else
                logger.LogWarning("Shell not available on this host: {ShellType}", shell.ShellType);
        }

        _advertisement = new ShellCapabilityAdvertisement(shells, DateTimeOffset.UtcNow);
        return _advertisement;
    }

    // ── Validation ───────────────────────────────────────────────────────────

    /// <summary>
    /// Validates that <paramref name="requestedShell"/> is available before a job executes.
    /// Returns <see cref="Result{T,Error}.Ok"/> with the matching <see cref="ShellInfo"/>,
    /// or a failure with <c>errorCategory=ENVIRONMENT</c> and the appropriate detailCode (AC-2, AC-3).
    /// </summary>
    public Result<ShellInfo, Error> ValidateShell(ShellType requestedShell)
    {
        if (_advertisement is null)
            return Result.Fail<ShellInfo>(
                Error.General("SHELL_001", "Shell capability advertisement not initialised. Call InitialiseAsync first."));

        var info = _advertisement.For(requestedShell);

        if (info is null || !info.IsAvailable)
        {
            return requestedShell == ShellType.WslBash
                ? Result.Fail<ShellInfo>(
                    Error.Environment(
                        "ENVIRONMENT",
                        "WSL Bash is not installed or not approved on this host.",
                        detailCode: "terminal.wslUnavailable"))
                : Result.Fail<ShellInfo>(
                    Error.Environment(
                        "ENVIRONMENT",
                        $"Shell '{requestedShell}' is not in the supported version list on this host.",
                        detailCode: "terminal.shellUnavailable"));
        }

        return Result.Ok(info);
    }

    /// <summary>Returns the cached advertisement, or null if <see cref="InitialiseAsync"/> has not been called.</summary>
    public ShellCapabilityAdvertisement? GetAdvertisement() => _advertisement;
}
