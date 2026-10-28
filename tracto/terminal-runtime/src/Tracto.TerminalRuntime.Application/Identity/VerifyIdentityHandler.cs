using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Identity;

namespace Tracto.TerminalRuntime.Application.Identity;

/// <summary>
/// Application-layer handler for <see cref="VerifyIdentityCommand"/>.
/// Selects Windows or WSL verification based on command type and delegates to
/// <see cref="IIdentityVerifier"/> (Infrastructure). Contains no process-launching logic.
/// </summary>
public sealed class VerifyIdentityHandler(IIdentityVerifier verifier)
{
    private const string WslBashCommandType = "wsl-bash";

    /// <summary>
    /// Verifies execution identity per the policy embedded in <paramref name="command"/>.
    /// Returns Ok with the confirmed <see cref="IdentityVerificationResult"/>, or a typed
    /// failure (VALIDATION / AUTHENTICATION / ENVIRONMENT / POLICY_DENIED).
    /// </summary>
    public async Task<Result<IdentityVerificationResult, Error>> HandleAsync(
        VerifyIdentityCommand command,
        CancellationToken ct = default)
    {
        var policy = command.Policy;

        if (command.CommandType.Equals(WslBashCommandType, StringComparison.OrdinalIgnoreCase))
        {
            // Validate required policy fields before delegating (fail-fast; AC-3)
            if (string.IsNullOrWhiteSpace(policy.WslDistro))
                return Result.Fail<IdentityVerificationResult>(
                    Error.Validation(
                        "IDENTITY_002",
                        "WSL distro must be specified in the identity policy for wsl-bash commands.",
                        detailCode: "terminal.missingWslDistro"));

            if (string.IsNullOrWhiteSpace(policy.WslUser))
                return Result.Fail<IdentityVerificationResult>(
                    Error.Validation(
                        "IDENTITY_003",
                        "WSL user must be specified in the identity policy for wsl-bash commands.",
                        detailCode: "terminal.missingWslUser"));

            return await verifier.VerifyWslIdentityAsync(
                policy.WslDistro,
                policy.WslUser,
                policy.OrchestratorWslDistro,
                ct);
        }
        else
        {
            // Windows path: cmd, powershell, etc.
            if (string.IsNullOrWhiteSpace(policy.WindowsIdentity))
                return Result.Fail<IdentityVerificationResult>(
                    Error.Validation(
                        "IDENTITY_001",
                        "Windows identity must be specified in the identity policy for Windows shell commands.",
                        detailCode: "terminal.missingWindowsIdentity"));

            return await verifier.VerifyWindowsIdentityAsync(policy.WindowsIdentity, ct);
        }
    }
}
