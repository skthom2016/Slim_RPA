namespace Tracto.TerminalRuntime.Domain.Cleanup;

/// <summary>
/// Tracks temporary files owned by a single run, together with the filesystem
/// roots inside which those files are permitted to live.
///
/// CEC v1.0.1 AC-2: files outside <see cref="ApprovedRoots"/> must never be
/// deleted, even if they appear in <see cref="OwnedFiles"/>.
/// </summary>
public sealed record TempFileRegistry(
    /// <summary>Absolute paths of all temporary files that the run created and owns.</summary>
    IReadOnlyList<string> OwnedFiles,

    /// <summary>
    /// Absolute directory paths that are authorised cleanup targets.
    /// A file is only eligible for deletion when its fully-resolved path
    /// starts with at least one of these roots.
    /// </summary>
    IReadOnlyList<string> ApprovedRoots);
