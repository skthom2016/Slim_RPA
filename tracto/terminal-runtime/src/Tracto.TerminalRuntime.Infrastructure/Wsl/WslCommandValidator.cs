using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.Wsl;

namespace Tracto.TerminalRuntime.Infrastructure.Wsl;

/// <summary>
/// Infrastructure implementation of <see cref="IWslCommandValidator"/>.
///
/// Enforces all four acceptance criteria without launching any process:
/// <list type="number">
///   <item>AC-2 – rejects a command targeting the Orchestrator distro.</item>
///   <item>AC-3 – rejects a command whose declared distro does not match the provisioned CustomerDistro.</item>
///   <item>AC-4 – rejects a script path outside the approved root inside the customer distro.</item>
///   <item>AC-1 – on all-pass, builds the wsl.exe ArgumentList: <c>-d {distro} -u {user} -- {script} [args]</c>.</item>
/// </list>
///
/// No plaintext secrets appear in error messages, argument lists, or logs (CEC invariant #4).
/// No process is launched from this class (CEC invariant #5 — business-logic only).
/// </summary>
public sealed class WslCommandValidator : IWslCommandValidator
{
    // wsl.exe lives in System32 on all modern Windows builds that ship WSL 2.
    private static readonly string WslExePath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "wsl.exe");

    private const string PolicyDeniedCode = "WSL_POLICY_001";

    /// <inheritdoc/>
    public Result<WslValidationResult, Error> Validate(ValidateWslCommandCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var policy = command.Policy;

        // ── AC-2: Reject if the declared distro is the Orchestrator distro ──────
        if (string.Equals(command.DeclaredDistro, policy.OrchestratorDistro,
                StringComparison.OrdinalIgnoreCase))
        {
            return Result<WslValidationResult, Error>.Fail(
                Error.PolicyDenied(
                    PolicyDeniedCode,
                    "Commands targeting the Orchestrator WSL distro are not permitted.",
                    detailCode: "wsl.orchestratorDistroRejected"));
        }

        // ── AC-3: Reject if declared distro does not match the CustomerDistro ────
        if (!string.Equals(command.DeclaredDistro, policy.CustomerDistro,
                StringComparison.OrdinalIgnoreCase))
        {
            return Result<WslValidationResult, Error>.Fail(
                Error.PolicyDenied(
                    PolicyDeniedCode,
                    "Declared WSL distro does not match the provisioned customer distro.",
                    detailCode: "wsl.distroMismatch"));
        }

        // ── AC-4: Reject if script path is outside the ApprovedScriptRoot ────────
        // Pre-check: reject any path that contains a '..' segment to prevent traversal
        // before the prefix check (mirrors the PathValidator pattern for Windows paths).
        if (ContainsDotDotSegment(command.ScriptPath))
        {
            return Result<WslValidationResult, Error>.Fail(
                Error.PolicyDenied(
                    PolicyDeniedCode,
                    "Script path contains a '..' traversal segment and was rejected before execution.",
                    detailCode: "wsl.scriptPathDenied"));
        }

        if (!IsUnderApprovedRoot(command.ScriptPath, policy.ApprovedScriptRoot))
        {
            return Result<WslValidationResult, Error>.Fail(
                Error.PolicyDenied(
                    PolicyDeniedCode,
                    "Script path is outside the approved script root for this customer distro.",
                    detailCode: "wsl.scriptPathDenied"));
        }

        // ── AC-1: All checks pass — construct the wsl.exe ArgumentList ───────────
        //
        // Format: wsl.exe -d <distro> -u <user> -- <scriptPath> [args...]
        // Using ArgumentList (not a combined string) avoids shell injection.
        var args = new List<string>
        {
            "-d", policy.CustomerDistro,
            "-u", policy.CustomerUser,
            "--",
            command.ScriptPath
        };

        foreach (var arg in command.ScriptArgs)
            args.Add(arg);

        return Result<WslValidationResult, Error>.Ok(
            new WslValidationResult(WslExePath, args.AsReadOnly()));
    }

    /// <summary>
    /// Returns true when <paramref name="path"/> contains a <c>..</c> segment,
    /// which would allow traversal outside the approved root.
    /// </summary>
    private static bool ContainsDotDotSegment(string path)
    {
        // Split on the Linux path separator (/) only; Windows separator is irrelevant for WSL paths.
        var segments = path.Split('/', StringSplitOptions.None);
        return Array.Exists(segments, s => s == "..");
    }

    /// <summary>
    /// Returns true when <paramref name="scriptPath"/> starts with
    /// <paramref name="approvedRoot"/> (Linux path prefix check, case-sensitive).
    ///
    /// A trailing slash is normalised on the root so that a root of
    /// <c>/opt/scripts</c> does not match <c>/opt/scripts-evil/run.sh</c>.
    /// </summary>
    private static bool IsUnderApprovedRoot(string scriptPath, string approvedRoot)
    {
        if (string.IsNullOrWhiteSpace(scriptPath) || string.IsNullOrWhiteSpace(approvedRoot))
            return false;

        // Ensure the root ends with '/' so prefix matching is segment-accurate.
        var normalizedRoot = approvedRoot.EndsWith('/') ? approvedRoot : approvedRoot + '/';

        // The script path must be the root itself, or a file/subdir below it.
        return scriptPath.StartsWith(normalizedRoot, StringComparison.Ordinal)
               || string.Equals(scriptPath, approvedRoot.TrimEnd('/'), StringComparison.Ordinal);
    }
}
