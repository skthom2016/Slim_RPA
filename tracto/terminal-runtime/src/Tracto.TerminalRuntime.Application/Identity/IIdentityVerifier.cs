using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Identity;

namespace Tracto.TerminalRuntime.Application.Identity;

/// <summary>
/// Resolves and verifies the effective execution identity against policy.
/// Implementations live in Infrastructure where process launching is permitted.
/// </summary>
public interface IIdentityVerifier
{
    /// <summary>
    /// Checks that the current Windows principal matches <paramref name="expectedIdentity"/>.
    /// Returns AUTHENTICATION / terminal.identityMismatch on mismatch (AC-1, AC-2).
    /// </summary>
    Task<Result<IdentityVerificationResult, Error>> VerifyWindowsIdentityAsync(
        string expectedIdentity,
        CancellationToken ct = default);

    /// <summary>
    /// Runs <c>wsl -d {distroName} --exec whoami</c> and verifies the result matches
    /// <paramref name="expectedUser"/>. Rejects <paramref name="distroName"/> if it equals
    /// <paramref name="orchestratorDistroName"/> (AC-4), returns ENVIRONMENT on unavailable distro (AC-3).
    /// </summary>
    Task<Result<IdentityVerificationResult, Error>> VerifyWslIdentityAsync(
        string distroName,
        string expectedUser,
        string? orchestratorDistroName,
        CancellationToken ct = default);
}
