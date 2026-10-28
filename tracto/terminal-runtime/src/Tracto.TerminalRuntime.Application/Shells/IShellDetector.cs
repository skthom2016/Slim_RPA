using Tracto.TerminalRuntime.Domain.Shells;

namespace Tracto.TerminalRuntime.Application.Shells;

/// <summary>
/// Detects which shells are available on the host machine and returns their version information.
/// Implementations live in the Infrastructure layer where process launching is permitted.
/// </summary>
public interface IShellDetector
{
    /// <summary>
    /// Probes all known shell types and returns one <see cref="ShellInfo"/> per type.
    /// Never throws; returns <see cref="ShellInfo.Unavailable"/> entries for shells that cannot be found.
    /// </summary>
    Task<IReadOnlyList<ShellInfo>> DetectAsync(CancellationToken cancellationToken = default);
}
