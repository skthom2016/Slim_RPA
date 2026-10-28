using Microsoft.Extensions.Logging;
using Tracto.TerminalRuntime.Application.Cleanup;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Cleanup;

namespace Tracto.TerminalRuntime.Infrastructure.Cleanup;

/// <summary>
/// Deletes temporary files owned by a completed or cancelled run.
///
/// Safety invariants (CEC v1.0.1 AC-1 and AC-2):
/// <list type="bullet">
///   <item>Only files whose fully-resolved path starts with an <see cref="TempFileRegistry.ApprovedRoots"/>
///         entry are deleted.  Files outside those roots are silently skipped.</item>
///   <item>Approved result files and diagnostics (files NOT in <see cref="TempFileRegistry.OwnedFiles"/>)
///         are never touched.</item>
///   <item>Path traversal: each candidate path is fully resolved with
///         <see cref="Path.GetFullPath"/> before the root-prefix check so that
///         <c>../</c> sequences cannot escape the approved roots.</item>
/// </list>
/// </summary>
public sealed class FilesystemCleanup(ILogger<FilesystemCleanup> logger) : IFilesystemCleanup
{
    public Task<Result<FilesystemCleanupResult, Error>> CleanupAsync(
        TempFileRegistry registry,
        CancellationToken ct = default)
    {
        if (registry is null)
            throw new ArgumentNullException(nameof(registry));

        var deleted   = new List<string>();
        int skipped   = 0;

        foreach (var raw in registry.OwnedFiles)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(raw))
                continue;

            string canonical;
            try
            {
                canonical = Path.GetFullPath(raw);
            }
            catch (Exception ex)
            {
                // AC-2: invalid paths that cannot be canonicalized are skipped safely.
                logger.LogWarning("Skipping temp-file path that could not be resolved: {Reason}",
                    ex.GetType().Name);
                skipped++;
                continue;
            }

            if (!IsInsideApprovedRoot(canonical, registry.ApprovedRoots))
            {
                // AC-2: never delete outside approved roots.
                logger.LogWarning(
                    "Skipping temp-file outside approved roots (path omitted to avoid leaking secrets).");
                skipped++;
                continue;
            }

            try
            {
                if (File.Exists(canonical))
                {
                    File.Delete(canonical);
                    deleted.Add(canonical);
                    // Deliberately not logging the path to avoid leaking temp secrets in log (CEC #4).
                    logger.LogDebug("Deleted one temporary file.");
                }
            }
            catch (Exception ex)
            {
                // Log a warning but continue — partial cleanup is better than aborting.
                logger.LogWarning("Failed to delete temporary file: {Reason}", ex.GetType().Name);
            }
        }

        var result = new FilesystemCleanupResult(
            FilesDeleted:              deleted.Count,
            FilesSkippedOutsideRoot:   skipped,
            DeletedPaths:              deleted.AsReadOnly());

        return Task.FromResult(Result<FilesystemCleanupResult, Error>.Ok(result));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static bool IsInsideApprovedRoot(string canonicalPath, IReadOnlyList<string> approvedRoots)
    {
        foreach (var root in approvedRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            string canonicalRoot;
            try
            {
                // Normalise the root the same way we normalise the file path.
                canonicalRoot = Path.GetFullPath(root).TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                continue;
            }

            // Require the file to be *inside* the root, not just a prefix match.
            // e.g. root=/tmp/runs must not match /tmp/runs-other/file.
            var rootWithSep = canonicalRoot + Path.DirectorySeparatorChar;
            if (canonicalPath.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(canonicalPath, canonicalRoot, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
