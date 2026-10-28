using Tracto.TerminalRuntime.Application.Arguments;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Arguments;

namespace Tracto.TerminalRuntime.Infrastructure.Arguments;

/// <summary>
/// Concrete <see cref="IArgumentValidator"/> implementation.
///
/// Validation order (fail-fast: first violation stops processing):
/// <list type="number">
///   <item>AC-5  — Argument value exceeds <see cref="CommandArgument.MaxLength"/>.</item>
///   <item>AC-6  — Argument value is a URL with a non-allowlisted host, or a filesystem path
///                 that does not fall under an approved root.</item>
/// </list>
///
/// Shell metacharacters (&amp;, |, ;, backticks, $(...)) in argument values are NOT treated
/// as an error here — the caller MUST use environment variables (TRACTO_ARG_*) so that
/// values are never interpolated into a shell command string.
/// </summary>
public sealed class ArgumentValidator : IArgumentValidator
{
    private const string TooLongDetailCode      = "terminal.argumentTooLong";
    private const string DisallowedUrlDetailCode = "terminal.disallowedUrlOrPath";

    /// <inheritdoc/>
    public Result<IReadOnlyList<CommandArgument>, Error> Validate(
        IReadOnlyList<CommandArgument> arguments,
        AllowedUrlRootsPolicy urlPolicy)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(urlPolicy);

        foreach (var arg in arguments)
        {
            // AC-5: max-length check.
            // NOTE: arg.Value.Length counts UTF-16 code units (char count in .NET).
            // For arguments that may contain supplementary-plane characters (surrogate
            // pairs, e.g. emoji), one code point contributes 2 to Length.  The declared
            // MaxLength must therefore be treated as a char-unit limit, not a byte limit.
            if (arg.MaxLength.HasValue && arg.Value.Length > arg.MaxLength.Value)
            {
                return Result<IReadOnlyList<CommandArgument>, Error>.Fail(
                    Error.Validation(
                        "ARGS_010",
                        // Do NOT include the raw value — no secrets / injection vectors in logs.
                        $"Argument '{arg.Name}' exceeds its declared maximum length of {arg.MaxLength.Value} characters.",
                        TooLongDetailCode));
            }

            // AC-6: URL/path allow-list check.
            var urlCheckResult = CheckUrlOrPath(arg, urlPolicy);
            if (urlCheckResult is not null)
                return Result<IReadOnlyList<CommandArgument>, Error>.Fail(urlCheckResult);
        }

        return Result<IReadOnlyList<CommandArgument>, Error>.Ok(arguments);
    }

    /// <inheritdoc/>
    public Result<string, Error> ValidatePath(
        string path,
        AllowedUrlRootsPolicy policy,
        string paramName,
        string detailCode)
    {
        ArgumentNullException.ThrowIfNull(policy);

        // Empty / whitespace paths pass here — the caller is responsible for the non-empty guard.
        if (string.IsNullOrWhiteSpace(path))
            return Result<string, Error>.Ok(path);

        // Relative or bare executable names (e.g. "cmd.exe", "pwsh.exe") are resolved by the OS
        // via PATH; they are not restricted by the path-roots policy.
        if (!IsAbsoluteFilesystemPath(path))
            return Result<string, Error>.Ok(path);

        // Absolute paths must fall under at least one approved root.
        if (!IsUnderAllowedRoot(path, policy.AllowedPathRoots))
        {
            return Result<string, Error>.Fail(
                Error.Validation(
                    "ARGS_020",
                    $"'{paramName}' references a filesystem path outside all approved roots.",
                    detailCode));
        }

        return Result<string, Error>.Ok(path);
    }

    // ── private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns an <see cref="Error"/> if the argument value looks like a disallowed
    /// URL or filesystem path, or <c>null</c> if the value is permitted.
    /// </summary>
    private static Error? CheckUrlOrPath(CommandArgument arg, AllowedUrlRootsPolicy policy)
    {
        var value = arg.Value;

        // Try URL check first (handles http://, https://, ftp://, etc.).
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            if (!IsAllowedHost(uri.Host, policy.AllowedHosts))
            {
                return Error.Validation(
                    "ARGS_011",
                    $"Argument '{arg.Name}' references a URL host that is not on the allow-list.",
                    DisallowedUrlDetailCode);
            }

            return null; // host is allowed — value is fine.
        }

        // Try filesystem path check (absolute paths only — relative paths are not restricted here).
        if (IsAbsoluteFilesystemPath(value))
        {
            if (!IsUnderAllowedRoot(value, policy.AllowedPathRoots))
            {
                return Error.Validation(
                    "ARGS_012",
                    $"Argument '{arg.Name}' references a filesystem path outside all approved roots.",
                    DisallowedUrlDetailCode);
            }
        }

        return null;
    }

    private static bool IsAllowedHost(string host, string[] allowedHosts)
    {
        if (allowedHosts.Length == 0) return false;
        return Array.Exists(
            allowedHosts,
            h => string.Equals(h, host, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsAbsoluteFilesystemPath(string value)
    {
        // Windows absolute: starts with drive letter + colon, or UNC \\
        // Unix absolute: starts with /
        if (string.IsNullOrEmpty(value)) return false;

        // UNC path
        if (value.StartsWith("\\\\", StringComparison.Ordinal)) return true;
        // Unix-style absolute
        if (value.StartsWith('/')) return true;
        // Windows drive-letter absolute: "C:\" or "C:/"
        if (value.Length >= 3
            && char.IsLetter(value[0])
            && value[1] == ':'
            && (value[2] == '\\' || value[2] == '/'))
        {
            return true;
        }

        return false;
    }

    internal static bool IsUnderAllowedRoot(string value, string[] allowedRoots)
    {
        if (allowedRoots.Length == 0) return false;

        string canonical;
        try { canonical = Path.GetFullPath(value); }
        catch { return false; }

        return Array.Exists(allowedRoots, root =>
        {
            if (string.IsNullOrWhiteSpace(root)) return false;
            string fullRoot;
            try { fullRoot = Path.GetFullPath(root); }
            catch { return false; }

            var normalizedRoot     = EnsureTrailing(fullRoot);
            var normalizedCanonical = EnsureTrailing(canonical);

            return normalizedCanonical.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(canonical, fullRoot, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static string EnsureTrailing(string p) =>
        p.EndsWith(Path.DirectorySeparatorChar) || p.EndsWith(Path.AltDirectorySeparatorChar)
            ? p
            : p + Path.DirectorySeparatorChar;
}
