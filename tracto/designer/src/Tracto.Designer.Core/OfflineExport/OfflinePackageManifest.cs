namespace Tracto.Designer.Core.OfflineExport;

public sealed record OfflinePackageManifest(
    Guid PackageId,
    DateTimeOffset CreatedAt,
    string ScriptId,
    string ScriptName,
    string ScriptVersion,
    string TargetRuntime,
    string MinRuntimeVersion,
    bool IsIsolated,
    string? SignatureDigest);
