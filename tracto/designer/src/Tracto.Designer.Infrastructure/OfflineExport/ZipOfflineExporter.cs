namespace Tracto.Designer.Infrastructure.OfflineExport;

using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Tracto.Designer.Core.OfflineExport;
using Tracto.Designer.Infrastructure.Serialization;

public sealed class ZipOfflineExporter : IOfflineExporter
{
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public async Task<OfflineExportResult> ExportAsync(OfflineExportRequest req, CancellationToken ct = default)
    {
        try
        {
            var packageId = Guid.NewGuid();
            var manifest = new OfflinePackageManifest(
                packageId, DateTimeOffset.UtcNow,
                req.Script.Id, req.Script.Name, req.Script.Version, req.Script.TargetRuntime,
                "1.0.0", req.IsIsolated, req.Signature?.Digest);

            var dir = Path.GetDirectoryName(req.OutputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(req.OutputPath)) File.Delete(req.OutputPath);

            using var zip = ZipFile.Open(req.OutputPath, ZipArchiveMode.Create);

            // manifest.json
            await WriteEntryAsync(zip, "manifest.json", JsonSerializer.Serialize(manifest, _json), ct);

            // script.json
            var scriptJson = new JsonScriptSerializer().Serialize(req.Script);
            await WriteEntryAsync(zip, "script.json", scriptJson, ct);

            // signature.json (if signed)
            if (req.Signature is not null)
                await WriteEntryAsync(zip, "signature.json", JsonSerializer.Serialize(req.Signature, _json), ct);

            // screenshots/ (if requested)
            if (req.IncludeScreenshots)
            {
                var sessionId = req.Script.Id;
                var screenshotDir = Path.Combine(Path.GetTempPath(), "tracto", sessionId);
                if (Directory.Exists(screenshotDir))
                {
                    foreach (var img in Directory.GetFiles(screenshotDir, "*.png"))
                    {
                        ct.ThrowIfCancellationRequested();
                        var entry = zip.CreateEntry($"screenshots/{Path.GetFileName(img)}", CompressionLevel.Optimal);
                        using var dst = entry.Open();
                        using var src = File.OpenRead(img);
                        await src.CopyToAsync(dst, ct).ConfigureAwait(false);
                    }
                }
            }

            return OfflineExportResult.Ok(req.OutputPath, packageId);
        }
        catch (OperationCanceledException) { return OfflineExportResult.Failure("Export cancelled."); }
        catch (Exception ex) { return OfflineExportResult.Failure($"Export failed: {ex.Message}"); }
    }

    private static async Task WriteEntryAsync(ZipArchive zip, string name, string content, CancellationToken ct)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), Encoding.UTF8);
        await w.WriteAsync(content.AsMemory(), ct).ConfigureAwait(false);
    }
}
