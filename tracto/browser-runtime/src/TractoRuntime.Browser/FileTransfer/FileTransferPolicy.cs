using Microsoft.Extensions.Logging;

namespace TractoRuntime.Browser.FileTransfer;

/// <summary>
/// Implements <see cref="IFileTransferPolicy"/> using canonical path comparison to ensure a
/// transfer source or destination is strictly nested under an approved root directory.
///
/// <para><b>BRW-US-013 AC#3</b> — Download destination must be under an approved root;
/// rejection happens before any bytes are written to the local filesystem.</para>
///
/// <para><b>BRW-US-013 AC#4</b> — Upload source path must be under an approved root;
/// rejection happens before the Playwright file transfer begins.</para>
///
/// <para>Path traversal attacks (e.g., <c>..\..</c> segments) are neutralised because
/// <see cref="Path.GetFullPath"/> resolves all relative segments to an absolute canonical
/// form before the prefix comparison is performed.</para>
/// </summary>
public sealed class FileTransferPolicy(
    FileTransferPolicyOptions options,
    ILogger<FileTransferPolicy> logger)
    : IFileTransferPolicy
{
    /// <inheritdoc/>
    public bool IsDownloadPathApproved(string destinationDirectory) =>
        IsPathUnderApprovedRoot(destinationDirectory, options.ApprovedDownloadRoots, "download", 3);

    /// <inheritdoc/>
    public bool IsUploadPathApproved(string sourcePath) =>
        IsPathUnderApprovedRoot(sourcePath, options.ApprovedUploadRoots, "upload", 4);

    // ─────────────────────────────────────────────────────────────────────────
    // Implementation
    // ─────────────────────────────────────────────────────────────────────────

    private bool IsPathUnderApprovedRoot(
        string path,
        IReadOnlyList<string> approvedRoots,
        string transferType,
        int acNumber)
    {
        // Canonicalise the subject path; reject if the OS refuses to resolve it.
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not canonicalise {TransferType} path '{Path}'; denying (BRW-US-013 AC#{Ac}).",
                transferType, path, acNumber);
            return false;
        }

        foreach (var root in approvedRoots)
        {
            string fullRoot;
            try
            {
                fullRoot = Path.GetFullPath(root);
            }
            catch
            {
                // Skip a root that the OS cannot resolve; it can never match any valid path.
                continue;
            }

            // Append a directory separator so that "C:\approved" does not match
            // "C:\approved-extra" (prefix collision).
            if (!fullRoot.EndsWith(Path.DirectorySeparatorChar))
                fullRoot += Path.DirectorySeparatorChar;

            // OrdinalIgnoreCase: Windows filesystem paths are case-insensitive; Path.GetFullPath
            // already normalises drive-letter casing so this comparison is safe on Windows.
            // The platform is Windows-only; a Linux-style case-sensitive comparison is not needed.
            if (fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        logger.LogWarning(
            "{TransferType} path '{Path}' is not under any approved root; " +
            "transfer denied (BRW-US-013 AC#{Ac}).",
            transferType, path, acNumber);

        return false;
    }
}
