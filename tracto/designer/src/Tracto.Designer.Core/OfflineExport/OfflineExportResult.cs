namespace Tracto.Designer.Core.OfflineExport;

public sealed record OfflineExportResult(
    bool Success,
    string? PackagePath = null,
    Guid? PackageId = null,
    string? ErrorMessage = null)
{
    public static OfflineExportResult Ok(string path, Guid id) => new(true, path, id);
    public static OfflineExportResult Failure(string reason) => new(false, ErrorMessage: reason);
}
