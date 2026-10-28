namespace Tracto.Designer.Core.OfflineExport;

public interface IOfflineExporter
{
    Task<OfflineExportResult> ExportAsync(OfflineExportRequest request, CancellationToken ct = default);
}
