namespace Tracto.TerminalRuntime.Application.Wsl;

/// <summary>
/// Abstracts the ability to query whether a named WSL distro is currently running.
/// Used by pre-execution checks (TER-US-017 AC-1) and by the background health
/// monitor that detects mid-execution outages (TER-US-017 AC-2).
/// </summary>
public interface IWslHealthMonitor
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="distroName"/> appears
    /// in the list of running WSL distros, <see langword="false"/> otherwise.
    /// </summary>
    Task<bool> IsDistroRunningAsync(string distroName, CancellationToken ct = default);
}
