namespace TractoRuntime.Browser.FileTransfer;

/// <summary>
/// Validates that file transfer source and destination paths reside within the
/// deployment-approved allowlist of root directories (WEB-006 / WEB-007 / BRW-US-013).
/// </summary>
/// <remarks>
/// Implementations must perform a pure path check only — no file system access, no
/// network requests, and no browser interactions are made during the check.
/// </remarks>
public interface IFileTransferPolicy
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="destinationDirectory"/> is nested
    /// under at least one of the configured approved download roots (BRW-US-013 AC#3).
    /// </summary>
    /// <param name="destinationDirectory">Absolute or relative path to the intended download destination directory.</param>
    bool IsDownloadPathApproved(string destinationDirectory);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="sourcePath"/> is nested under at
    /// least one of the configured approved upload roots (BRW-US-013 AC#4).
    /// </summary>
    /// <param name="sourcePath">Absolute or relative path to the file to be uploaded.</param>
    bool IsUploadPathApproved(string sourcePath);
}
