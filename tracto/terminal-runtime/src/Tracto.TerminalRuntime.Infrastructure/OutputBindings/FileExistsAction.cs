using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Tracto.TerminalRuntime.Application;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.OutputBindings;
using Tracto.TerminalRuntime.Domain.OutputBindings;
using Tracto.TerminalRuntime.Domain.Policies;

namespace Tracto.TerminalRuntime.Infrastructure.OutputBindings;

/// <summary>
/// Implements <c>terminal.file.exists</c>.
///
/// Acceptance criteria:
/// <list type="bullet">
///   <item>(AC-3) Successful path checks for existing files are evidenced with a
///         signed reference (canonical path + SHA-256 digest) appended to the run
///         journal via <see cref="IRunJournal.AppendOutputArtifactRefAsync"/>.</item>
///   <item>(AC-4) Path within the approved storage root → returns a boolean result.</item>
///   <item>(AC-4) Path outside the approved storage root → <c>errorCategory=POLICY_DENIED</c>
///         is returned <em>before</em> any stat operation.</item>
///   <item>(AC-4) A path that contains a secret segment must not expose that segment
///         in the error detail (secret sanitisation is the caller's responsibility;
///         the error message here omits the raw path).</item>
/// </list>
/// </summary>
public sealed class FileExistsAction : IFileExistsAction
{
    private static readonly char[] PathSeparators = ['/', '\\'];
    private const string PolicyDeniedDetailCode = "terminal.storageRootDenied";

    private readonly IRunJournal _journal;
    private readonly ILogger<FileExistsAction> _logger;

    public FileExistsAction(IRunJournal journal, ILogger<FileExistsAction> logger)
    {
        _journal = journal;
        _logger  = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<FileExistsResult, Error>> CheckAsync(
        string path,
        StorageRootPolicy policy,
        string runId,
        string attemptId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(runId);
        ArgumentNullException.ThrowIfNull(attemptId);

        // Phase 1 — pre-resolution dot-dot check (same pattern as PathValidator).
        var segments = path.Split(PathSeparators, StringSplitOptions.None);
        if (Array.Exists(segments, s => s == ".."))
        {
            return Result<FileExistsResult, Error>.Fail(
                Error.PolicyDenied(
                    "terminal.file.exists.pathPolicy",
                    "Path contains a '..' traversal segment and was rejected before resolution.",
                    PolicyDeniedDetailCode));
        }

        // Phase 2 — canonicalize.
        string canonical;
        try
        {
            canonical = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Result<FileExistsResult, Error>.Fail(
                Error.PolicyDenied(
                    "terminal.file.exists.pathPolicy",
                    "Path could not be canonicalized.",
                    PolicyDeniedDetailCode));
        }

        // Phase 3 — approved storage root check (before any I/O).
        if (!IsUnderApprovedRoot(canonical, policy.ApprovedRoots))
        {
            // Do NOT include the raw path in the error message to avoid leaking a secret segment.
            return Result<FileExistsResult, Error>.Fail(
                Error.PolicyDenied(
                    "terminal.file.exists.pathPolicy",
                    "Path resolves outside all approved storage roots.",
                    PolicyDeniedDetailCode));
        }

        // Phase 4 — safe to perform filesystem stat now.
        var exists = File.Exists(canonical);

        // Phase 5 — AC-3: evidence existing files with a signed artifact reference.
        if (exists)
        {
            await RecordArtifactRefAsync(runId, attemptId, canonical, ct).ConfigureAwait(false);
        }

        return Result<FileExistsResult, Error>.Ok(new FileExistsResult(canonical, exists));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes the SHA-256 digest of a file and appends the artifact reference
    /// to the run journal. Failures are logged at Warning level but are non-fatal:
    /// the file-existence result is returned to the caller regardless.
    /// </summary>
    private async Task RecordArtifactRefAsync(
        string runId,
        string attemptId,
        string canonicalPath,
        CancellationToken ct)
    {
        try
        {
            string sha256Hex;
            await using (var stream = File.OpenRead(canonicalPath))
            {
                var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
                sha256Hex = Convert.ToHexString(hash).ToLowerInvariant();
            }

            var journalResult = await _journal
                .AppendOutputArtifactRefAsync(runId, attemptId, canonicalPath, sha256Hex, ct)
                .ConfigureAwait(false);

            if (journalResult.IsFailure)
            {
                _logger.LogWarning(
                    "Failed to record output artifact ref for run {RunId}: {Error}",
                    runId, journalResult.Error.Message);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Journal evidence failure must not surface to the caller.
            _logger.LogWarning(ex,
                "Unexpected error recording artifact ref for run {RunId} path {Path}.",
                runId, "<omitted>");
        }
    }

    private static bool IsUnderApprovedRoot(string canonical, string[] approvedRoots)
    {
        if (approvedRoots.Length == 0)
            return false;

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
