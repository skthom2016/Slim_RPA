using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Cleanup;

namespace Tracto.TerminalRuntime.Application.Cleanup;

/// <summary>
/// Contract for removing temporary files that a run created.
///
/// Invariants (CEC v1.0.1 AC-1 and AC-2):
/// <list type="bullet">
///   <item>Only files listed in <see cref="TempFileRegistry.OwnedFiles"/> are candidates for deletion.</item>
///   <item>A file is only deleted when its canonical path resides inside one of
///         <see cref="TempFileRegistry.ApprovedRoots"/> — no file outside those roots may be
///         deleted under any circumstances.</item>
///   <item>Approved result files and diagnostics that the run marked as preserved must not be removed.</item>
///   <item>No temporary secrets may remain on disk after cleanup completes.</item>
/// </list>
/// </summary>
public interface IFilesystemCleanup
{
    /// <summary>
    /// Deletes all eligible temporary files in <paramref name="registry"/>.
    ///
    /// Returns the number of files actually deleted.
    /// Files outside <see cref="TempFileRegistry.ApprovedRoots"/> are silently skipped;
    /// they are never deleted.
    /// </summary>
    Task<Result<FilesystemCleanupResult, Error>> CleanupAsync(
        TempFileRegistry registry,
        CancellationToken ct = default);
}

/// <summary>Summary of a filesystem cleanup operation.</summary>
public sealed record FilesystemCleanupResult(
    int FilesDeleted,
    int FilesSkippedOutsideRoot,
    IReadOnlyList<string> DeletedPaths);
