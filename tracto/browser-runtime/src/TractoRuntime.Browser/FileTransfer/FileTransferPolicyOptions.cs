namespace TractoRuntime.Browser.FileTransfer;

/// <summary>
/// Deployment-level configuration for approved file transfer path roots (BRW-US-013).
/// Both lists must be set in the deployment policy; empty lists deny all transfers.
/// </summary>
public sealed record FileTransferPolicyOptions
{
    /// <summary>
    /// Approved root directories for downloaded files (WEB-007 / BRW-US-013 AC#3).
    /// A download destination must be nested under one of these roots; otherwise the
    /// download is rejected before any bytes are written.
    /// </summary>
    public IReadOnlyList<string> ApprovedDownloadRoots { get; init; } = [];

    /// <summary>
    /// Approved root directories for uploaded files (WEB-006 / BRW-US-013 AC#4).
    /// An upload source path must reside under one of these roots; otherwise the
    /// transfer is rejected before it begins.
    /// </summary>
    public IReadOnlyList<string> ApprovedUploadRoots { get; init; } = [];
}
