namespace Tracto.Designer.Core.OfflineExport;

using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Core.Signing;

public sealed record OfflineExportRequest(
    Script Script,
    ScriptSignature? Signature,
    string OutputPath,
    bool IsIsolated = false,
    bool IncludeScreenshots = false);
