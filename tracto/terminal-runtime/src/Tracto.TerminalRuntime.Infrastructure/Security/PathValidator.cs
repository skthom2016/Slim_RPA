using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.Interfaces;
using Tracto.TerminalRuntime.Domain.Policies;

namespace Tracto.TerminalRuntime.Infrastructure.Security;

/// <summary>
/// Validates working-directory references against a <see cref="WorkingDirPolicy"/>.
///
/// Rejection is performed in two strict phases:
/// <list type="number">
///   <item>
///     <b>Pre-resolution (AC-2):</b> The raw <c>cwdRef</c> is split on both <c>/</c> and <c>\</c>
///     path separators. If any segment equals <c>..</c> the request is immediately rejected —
///     regardless of what the canonicalized form would be.
///   </item>
///   <item>
///     <b>Post-canonicalization (AC-1):</b> The canonicalized (absolute) path must be a subtree of
///     at least one approved root declared in <see cref="WorkingDirPolicy.ApprovedRoots"/>.
///   </item>
/// </list>
/// Both rejection categories return errorCategory=POLICY_DENIED and
/// detailCode=terminal.pathTraversalDenied.
/// </summary>
public sealed class PathValidator : IPathValidator
{
    private static readonly char[] PathSeparators = ['/', '\\'];
    private const string PolicyDeniedDetailCode = "terminal.pathTraversalDenied";

    /// <inheritdoc/>
    public Result<string, Error> Validate(string cwdRef, WorkingDirPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(cwdRef);
        ArgumentNullException.ThrowIfNull(policy);

        // Phase 1 — pre-resolution dot-dot check (AC-2).
        // Split on both path separators and reject if any segment is literally "..".
        var segments = cwdRef.Split(PathSeparators, StringSplitOptions.None);
        if (Array.Exists(segments, s => s == ".."))
        {
            return Result<string, Error>.Fail(
                Error.PolicyDenied(
                    "terminal.pathPolicy",
                    "Working directory reference contains a '..' traversal segment and was rejected before path resolution.",
                    PolicyDeniedDetailCode));
        }

        // Phase 2 — canonicalization and approved-root check (AC-1).
        string canonical;
        try
        {
            canonical = Path.GetFullPath(cwdRef);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Result<string, Error>.Fail(
                Error.PolicyDenied(
                    "terminal.pathPolicy",
                    "Working directory reference could not be canonicalized.",
                    PolicyDeniedDetailCode));
        }

        if (!IsUnderApprovedRoot(canonical, policy.ApprovedRoots))
        {
            return Result<string, Error>.Fail(
                Error.PolicyDenied(
                    "terminal.pathPolicy",
                    "Working directory reference resolves outside all approved roots.",
                    PolicyDeniedDetailCode));
        }

        return Result<string, Error>.Ok(canonical);
    }

    /// <summary>
    /// Returns true when <paramref name="canonical"/> is equal to, or a direct subtree of,
    /// at least one entry in <paramref name="approvedRoots"/>.
    /// Comparison is case-insensitive to handle Windows file system semantics.
    /// </summary>
    private static bool IsUnderApprovedRoot(string canonical, string[] approvedRoots)
    {
        if (approvedRoots.Length == 0)
            return false;

        // Normalize so the canonical path always ends with a directory separator.
        // Prevents a root of "C:\work" from matching "C:\workspaces\evil".
        var normalizedCanonical = EnsureTrailingSeparator(canonical);

        return Array.Exists(approvedRoots, root =>
        {
            if (string.IsNullOrWhiteSpace(root))
                return false;

            string fullRoot;
            try { fullRoot = Path.GetFullPath(root); }
            catch { return false; }

            var normalizedRoot = EnsureTrailingSeparator(fullRoot);
            return normalizedCanonical.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(canonical, fullRoot, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
}
