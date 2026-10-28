namespace Tracto.Designer.Unit.Tests.OfflineExport;

using System.IO;
using System.IO.Compression;
using FluentAssertions;
using Tracto.Designer.Core.OfflineExport;
using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Infrastructure.OfflineExport;

public class OfflineExportTests
{
    private static Script MakeScript()
    {
        var s = new Script { Id = "offline-test", Name = "Offline Test", Version = "1.0.0", TargetRuntime = "desktop" };
        s.Steps.Add(new ScriptStep { StepNumber = 1, Description = "Click", Action = "click" });
        return s;
    }

    [Fact]
    public async Task Export_Creates_Zip_With_Manifest_And_Script()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"tracto-offline-{Guid.NewGuid():N}");
        var path = Path.Combine(dir, "test.zip");
        var req = new OfflineExportRequest(MakeScript(), null, path);
        var r = await new ZipOfflineExporter().ExportAsync(req);
        r.Success.Should().BeTrue();
        r.PackagePath.Should().Be(path);
        File.Exists(path).Should().BeTrue();
        using (var zip = ZipFile.OpenRead(path))
        {
            zip.GetEntry("manifest.json").Should().NotBeNull();
            zip.GetEntry("script.json").Should().NotBeNull();
        }
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task Export_Without_Signature_No_Signature_File()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tracto-nosig-{Guid.NewGuid():N}.zip");
        var req = new OfflineExportRequest(MakeScript(), null, path);
        await new ZipOfflineExporter().ExportAsync(req);
        using (var zip = ZipFile.OpenRead(path))
        {
            zip.GetEntry("signature.json").Should().BeNull();
        }
        File.Delete(path);
    }

    [Fact]
    public async Task Export_Manifest_Contains_Correct_Fields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tracto-mf-{Guid.NewGuid():N}.zip");
        var s = MakeScript();
        await new ZipOfflineExporter().ExportAsync(new OfflineExportRequest(s, null, path, IsIsolated: true));
        string json;
        using (var zip = ZipFile.OpenRead(path))
        {
            var mf = zip.GetEntry("manifest.json")!;
            using var rdr = new StreamReader(mf.Open());
            json = await rdr.ReadToEndAsync();
        }
        // WriteIndented=true produces "ScriptId": "offline-test" (with space after colon)
        json.Should().Contain("\"ScriptId\": \"offline-test\"");
        json.Should().Contain("\"IsIsolated\": true");
        File.Delete(path);
    }

    [Fact]
    public async Task Export_Cancelled_Returns_Failure()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var r = await new ZipOfflineExporter().ExportAsync(
            new OfflineExportRequest(MakeScript(), null, "C:\\noop.zip"), cts.Token);
        r.Success.Should().BeFalse();
    }

    [Fact]
    public void OfflineExportResult_Ok_Factory()
    {
        var id = Guid.NewGuid();
        var r = OfflineExportResult.Ok("path.zip", id);
        r.Success.Should().BeTrue();
        r.PackagePath.Should().Be("path.zip");
        r.PackageId.Should().Be(id);
    }

    [Fact]
    public void OfflineExportResult_Failure_Factory()
    {
        var r = OfflineExportResult.Failure("oops");
        r.Success.Should().BeFalse();
        r.ErrorMessage.Should().Be("oops");
    }

    [Fact]
    public void OfflinePackageManifest_IsIsolated_Field()
    {
        var m = new OfflinePackageManifest(Guid.NewGuid(), DateTimeOffset.UtcNow, "id", "n", "1.0.0", "desktop", "1.0.0", true, null);
        m.IsIsolated.Should().BeTrue();
    }
}
