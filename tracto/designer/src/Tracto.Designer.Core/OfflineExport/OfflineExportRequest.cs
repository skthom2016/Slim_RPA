namespace Tracto.Designer.Core.OfflineExport;

using Tracto.Designer.Core.Scripting;

public sealed record OfflineExportRequest(
    Script Script,
    string OutputPath,
    bool IsIsolated = false,
    bool IncludeScreenshots = false);
