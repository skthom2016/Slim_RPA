using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.OutputBindings;
using Tracto.TerminalRuntime.Domain.Policies;

namespace Tracto.TerminalRuntime.Application.OutputBindings;

/// <summary>
/// Implements the <c>terminal.file.exists</c> built-in action.
/// Path validation against the approved storage root is performed before any stat
/// operation, so a policy-denied path never triggers filesystem I/O (AC-4).
/// On a successful check of an existing file, a signed artifact reference
/// (canonical path + SHA-256 digest) is appended to the run journal (AC-3).
/// </summary>
public interface IFileExistsAction
{
    /// <summary>
    /// Checks whether <paramref name="path"/> exists as a file on the local filesystem
    /// and, when the file is found, records a signed artifact reference in the run journal.
    /// </summary>
    /// <param name="path">The path supplied by the step definition.</param>
    /// <param name="policy">The storage root policy to validate the path against.</param>
    /// <param name="runId">Logical run identifier used to locate the journal entry.</param>
    /// <param name="attemptId">Current attempt identifier for the journal entry.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///   <see cref="FileExistsResult"/> on success, or a failure with
    ///   <c>errorCategory=POLICY_DENIED</c> when the path falls outside the approved
    ///   storage root — without exposing any secret segment in the error detail (AC-4).
    /// </returns>
    Task<Result<FileExistsResult, Error>> CheckAsync(
        string path,
        StorageRootPolicy policy,
        string runId,
        string attemptId,
        CancellationToken ct = default);
}
