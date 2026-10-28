using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.PowerShell;
using Tracto.TerminalRuntime.Domain.Policies;

namespace Tracto.TerminalRuntime.Infrastructure.PowerShell;

/// <summary>
/// Infrastructure implementation of <see cref="IPowerShellPolicyValidator"/>.
/// Performs all policy checks before any PowerShell process is launched:
///   - No -ExecutionPolicy Bypass in extra args
///   - No Invoke-Expression or forbidden cmdlets in script text (static scan)
///   - Module imports restricted to approved allowlist
///   - Language mode enforced
///   - Script integrity hash verified (SHA-256)
/// </summary>
public sealed partial class PowerShellPolicyValidator(
    IOptions<PowerShellPolicySettings> options,
    ILogger<PowerShellPolicyValidator> logger) : IPowerShellPolicyValidator
{
    private readonly PowerShellPolicySettings _settings = options.Value;

    // ── Regex: detect -ExecutionPolicy Bypass anywhere in an argument string ──────
    [GeneratedRegex(@"-ExecutionPolicy\s+Bypass", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExecutionPolicyBypassPattern();

    // ── Regex: detect -ExecutionPolicy Unrestricted ──────────────────────────────
    [GeneratedRegex(@"-ExecutionPolicy\s+Unrestricted", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExecutionPolicyUnrestrictedPattern();

    // ── Regex: detect Invoke-Expression / iex calls ───────────────────────────────
    [GeneratedRegex(@"\b(Invoke-Expression|iex)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InvokeExpressionPattern();

    // ── Regex: detect Import-Module calls ────────────────────────────────────────
    [GeneratedRegex(@"\bImport-Module\s+(?<name>[A-Za-z0-9._-]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImportModulePattern();

    // ── Regex: detect argument injection characters ───────────────────────────────
    [GeneratedRegex(@"[;&|`$(){}\[\]<>""']", RegexOptions.CultureInvariant)]
    private static partial Regex ArgumentInjectionPattern();

    public async Task<Result<PowerShellValidationResult, Error>> ValidateAsync(
        ValidatePowerShellCommandCommand command,
        CancellationToken ct = default)
    {
        // ── 1. Scan extra command-line args for -ExecutionPolicy Bypass ───────────
        var bypassCheck = CheckForBypassFlag(command.ExtraCommandLineArgs);
        if (bypassCheck is not null)
            return Result.Fail<PowerShellValidationResult>(bypassCheck);

        // ── 2. Check declared module imports against allowlist ────────────────────
        var moduleCheck = CheckModuleAllowlist(command.ModulesToImport, command.Policy.AllowedModules);
        if (moduleCheck is not null)
            return Result.Fail<PowerShellValidationResult>(moduleCheck);

        // ── 3. Validate argument values for injection characters ──────────────────
        var argCheck = CheckArgumentValues(command.Arguments);
        if (argCheck is not null)
            return Result.Fail<PowerShellValidationResult>(argCheck);

        // ── 4. Validate script path against approved roots (Fix: CEC path invariant) ─
        var rootCheck = CheckScriptPathRoot(command.ScriptPath, _settings.ApprovedScriptRoots);
        if (rootCheck is not null)
            return Result.Fail<PowerShellValidationResult>(rootCheck);

        // ── 5. Read the script file ONCE as bytes (Fix: eliminates TOCTOU) ────────
        //       Hash verification runs first so a tampered file is rejected before
        //       the text scan processes any attacker-controlled content.
        byte[] fileBytes;
        try
        {
            fileBytes = await File.ReadAllBytesAsync(command.ScriptPath, ct);
        }
        catch (FileNotFoundException)
        {
            return Result.Fail<PowerShellValidationResult>(
                Error.Validation(
                    "PS_POLICY_008",
                    "Script file not found. Only pre-approved, versioned scripts are permitted.",
                    detailCode: "powershell.scriptNotFound"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not read script file {ScriptPath}.", command.ScriptPath);
            return Result.Fail<PowerShellValidationResult>(
                Error.General(
                    "PS_POLICY_009",
                    "Could not read the PowerShell script file for policy scanning.",
                    detailCode: "powershell.scriptReadError"));
        }

        // ── 6. Verify script integrity hash FIRST (fail-fast on tampered file) ────
        if (command.ScriptIntegrityHash is not null)
        {
            var integrityCheck = VerifyScriptIntegrity(fileBytes, command.ScriptIntegrityHash);
            if (integrityCheck is not null)
                return Result.Fail<PowerShellValidationResult>(integrityCheck);
        }
        else if (!_settings.SkipIntegrityCheckInDevelopment)
        {
            return Result.Fail<PowerShellValidationResult>(
                Error.PolicyDenied(
                    "PS_POLICY_010",
                    "Script integrity hash is required but was not provided. Unsigned scripts are not permitted.",
                    detailCode: "powershell.missingIntegrityHash"));
        }

        // ── 7. Scan script text for forbidden constructs + module imports ──────────
        //       Text is decoded from the already-read, hash-verified bytes — no second read.
        var scriptContent = Encoding.UTF8.GetString(fileBytes);
        var scriptScanResult = ScanScriptContent(scriptContent, command.Policy.AllowedModules);
        if (scriptScanResult is not null)
            return Result.Fail<PowerShellValidationResult>(scriptScanResult);

        // ── 8. Build the safe pwsh.exe argument list ──────────────────────────────
        var safeArgs = BuildSafeArguments(command);
        var result = new PowerShellValidationResult(safeArgs, command.Policy.LanguageMode);

        logger.LogInformation(
            "PowerShell policy validation passed for script {ScriptPath}; LanguageMode={LanguageMode}.",
            command.ScriptPath,
            result.EffectiveLanguageMode);

        return Result.Ok(result);
    }

    // ── Private helpers ──────────────────────────────────────────────────────────

    private static Error? CheckForBypassFlag(IReadOnlyList<string> extraArgs)
    {
        // Check each argument individually and also the full joined string
        // (some callers pass "-ExecutionPolicy Bypass" as a single string)
        var fullArgs = string.Join(" ", extraArgs);

        if (ExecutionPolicyBypassPattern().IsMatch(fullArgs))
            return Error.PolicyDenied(
                "PS_POLICY_004",
                "The -ExecutionPolicy Bypass flag is explicitly prohibited by deployment policy.",
                detailCode: "powershell.executionPolicyBypass");

        if (ExecutionPolicyUnrestrictedPattern().IsMatch(fullArgs))
            return Error.PolicyDenied(
                "PS_POLICY_005",
                "The -ExecutionPolicy Unrestricted flag is explicitly prohibited by deployment policy.",
                detailCode: "powershell.executionPolicyUnrestricted");

        // Check for individual args that are just "Bypass" or "Unrestricted"
        // following a previous -ExecutionPolicy token
        for (int i = 0; i < extraArgs.Count - 1; i++)
        {
            if (extraArgs[i].Equals("-ExecutionPolicy", StringComparison.OrdinalIgnoreCase))
            {
                var val = extraArgs[i + 1];
                if (PowerShellPolicy.ForbiddenExecutionPolicies.Contains(val))
                    return Error.PolicyDenied(
                        "PS_POLICY_004",
                        $"Execution policy '{val}' is explicitly prohibited by deployment policy.",
                        detailCode: "powershell.executionPolicyBypass");
            }
        }

        return null;
    }

    private static Error? CheckModuleAllowlist(
        IReadOnlyList<string> modules,
        string[] allowedModules)
    {
        if (modules.Count == 0)
            return null;

        var allowedSet = new HashSet<string>(allowedModules, StringComparer.OrdinalIgnoreCase);

        foreach (var module in modules)
        {
            if (!allowedSet.Contains(module))
                return Error.PolicyDenied(
                    "PS_POLICY_006",
                    $"PowerShell module '{module}' is not in the approved module allowlist.",
                    detailCode: "powershell.moduleNotAllowed");
        }

        return null;
    }

    private static Error? CheckArgumentValues(IReadOnlyDictionary<string, string> arguments)
    {
        foreach (var (key, value) in arguments)
        {
            if (ArgumentInjectionPattern().IsMatch(value))
                return Error.Validation(
                    "PS_POLICY_007",
                    $"Argument '{key}' contains shell metacharacters that could enable injection. Only typed scalar values are permitted.",
                    detailCode: "powershell.argumentInjectionRisk");
        }

        return null;
    }

    /// <summary>
    /// Verifies that <paramref name="scriptPath"/>'s fully-resolved absolute path begins with
    /// at least one of the configured <paramref name="approvedRoots"/>.  When
    /// <paramref name="approvedRoots"/> is empty the check is skipped (development convenience;
    /// production settings must supply at least one root).
    /// </summary>
    private static Error? CheckScriptPathRoot(string scriptPath, string[] approvedRoots)
    {
        if (approvedRoots.Length == 0)
            return null; // no roots configured — allow (development mode)

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(scriptPath);
        }
        catch
        {
            return Error.PolicyDenied(
                "PS_POLICY_015",
                "Script path could not be resolved to a canonical absolute path.",
                detailCode: "powershell.unapprovedScriptRoot");
        }

        foreach (var root in approvedRoots)
        {
            // Normalise the root so it ends with a separator — prevents a root of
            // "C:\Scripts" from matching "C:\ScriptsEvil\bad.ps1".
            var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                 + Path.DirectorySeparatorChar;

            if (fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                return null; // within an approved root
        }

        return Error.PolicyDenied(
            "PS_POLICY_015",
            "Script path is outside the approved script root. Only scripts located under a configured approved root directory are permitted.",
            detailCode: "powershell.unapprovedScriptRoot");
    }

    /// <summary>
    /// Verifies SHA-256 integrity of already-read <paramref name="fileBytes"/> against
    /// <paramref name="expectedHash"/>.  No file I/O — caller is responsible for reading.
    /// </summary>
    private static Error? VerifyScriptIntegrity(byte[] fileBytes, string expectedHash)
    {
        var actualHash = Convert.ToHexString(SHA256.HashData(fileBytes)).ToLowerInvariant();
        var normalized = expectedHash.Trim().ToLowerInvariant();

        if (!string.Equals(actualHash, normalized, StringComparison.Ordinal))
            return Error.PolicyDenied(
                "PS_POLICY_014",
                "Script integrity check failed. The script file has been modified or is unsigned.",
                detailCode: "powershell.integrityCheckFailed");

        return null;
    }

    /// <summary>
    /// Scans already-decoded <paramref name="scriptContent"/> for forbidden constructs and
    /// unapproved module imports.  No file I/O.
    /// </summary>
    private static Error? ScanScriptContent(string scriptContent, string[] allowedModules)
    {
        // ── Scan for Invoke-Expression / iex ────────────────────────────────
        if (InvokeExpressionPattern().IsMatch(scriptContent))
            return Error.PolicyDenied(
                "PS_POLICY_011",
                "The script contains 'Invoke-Expression' (iex), which is unconditionally prohibited. Only pre-approved script references with typed arguments are allowed.",
                detailCode: "powershell.invokeExpressionForbidden");

        // ── Scan for forbidden cmdlets by name ──────────────────────────────
        foreach (var forbidden in PowerShellPolicy.ForbiddenCmdlets)
        {
            // Skip iex/Invoke-Expression — already handled with the combined pattern
            if (forbidden.Equals("Invoke-Expression", StringComparison.OrdinalIgnoreCase) ||
                forbidden.Equals("iex", StringComparison.OrdinalIgnoreCase))
                continue;

            var pattern = new Regex($@"\b{Regex.Escape(forbidden)}\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (pattern.IsMatch(scriptContent))
                return Error.PolicyDenied(
                    "PS_POLICY_012",
                    $"The script contains forbidden cmdlet '{forbidden}', which is not permitted by deployment policy.",
                    detailCode: "powershell.forbiddenCmdlet");
        }

        // ── Scan for -ExecutionPolicy Bypass inside the script text ────────
        if (ExecutionPolicyBypassPattern().IsMatch(scriptContent))
            return Error.PolicyDenied(
                "PS_POLICY_013",
                "The script contains '-ExecutionPolicy Bypass', which is prohibited.",
                detailCode: "powershell.executionPolicyBypass");

        // ── Scan Import-Module calls and check against allowlist ────────────
        var allowedSet = new HashSet<string>(allowedModules, StringComparer.OrdinalIgnoreCase);
        foreach (Match m in ImportModulePattern().Matches(scriptContent))
        {
            var moduleName = m.Groups["name"].Value;
            if (!allowedSet.Contains(moduleName))
                return Error.PolicyDenied(
                    "PS_POLICY_006",
                    $"Script imports module '{moduleName}' which is not in the approved module allowlist.",
                    detailCode: "powershell.moduleNotAllowed");
        }

        return null;
    }

    private IReadOnlyList<string> BuildSafeArguments(ValidatePowerShellCommandCommand command)
    {
        // Effective execution policy: always use the policy setting, never what the caller requested
        var effectiveExecPolicy = _settings.ExecutionPolicy;

        // Guard: if somehow the configuration is corrupted with Bypass, fall back to RemoteSigned
        if (PowerShellPolicy.ForbiddenExecutionPolicies.Contains(effectiveExecPolicy))
        {
            logger.LogWarning(
                "Configuration ExecutionPolicy '{Configured}' is forbidden; falling back to RemoteSigned.",
                effectiveExecPolicy);
            effectiveExecPolicy = "RemoteSigned";
        }

        var args = new List<string>
        {
            "-NonInteractive",
            "-NoProfile",
            "-ExecutionPolicy", effectiveExecPolicy,
            "-File", command.ScriptPath
        };

        // Append typed arguments as -Key Value pairs (already sanitized above)
        foreach (var (key, value) in command.Arguments)
        {
            args.Add($"-{key}");
            args.Add(value);
        }

        return args.AsReadOnly();
    }
}
