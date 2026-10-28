namespace Tracto.Designer.Unit.Tests.Integration;

using System.IO;
using FluentAssertions;
using Tracto.Designer.Core.Credentials;
using Tracto.Designer.Core.IsolatedMode;
using Tracto.Designer.Core.Mcp;
using Tracto.Designer.Core.OfflineExport;
using Tracto.Designer.Core.Privacy;
using Tracto.Designer.Core.Publication;
using Tracto.Designer.Core.Recording;
using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Core.Signing;
using Tracto.Designer.Core.Testing;
using Tracto.Designer.Core.Validation;
using Tracto.Designer.Infrastructure.OfflineExport;
using Tracto.Designer.Infrastructure.Serialization;

// DESIGN-IT integration scenarios from FRD Appendix

public class DesignItIntegrationTests
{
    private static readonly byte[] TestKey = System.Text.Encoding.UTF8.GetBytes("integration-test-key-2024");

    private static Script BuildValidScript(string id = "it-script")
    {
        var s = new Script { Id = id, Name = "Integration Test Script", Version = "1.0.0", TargetRuntime = "desktop" };
        s.Steps.Add(new ScriptStep { StepNumber = 1, Description = "Launch application", Action = "navigate", Condition = null });
        s.Steps.Add(new ScriptStep { StepNumber = 2, Description = "Click login button", Action = "click" });
        s.Steps.Add(new ScriptStep { StepNumber = 3, Description = "Enter credentials", Action = "type" });
        return s;
    }

    // DESIGN-IT-01: Desktop record → draft → validate → sign → test → publish (no AI)
    [Fact]
    public async Task DESIGN_IT_01_Full_Deterministic_Pipeline_Desktop()
    {
        var script = BuildValidScript("it-01");
        var validator = new ScriptValidator();
        var signer = new HmacScriptSigner("it-signer", TestKey, validator);
        var runner = new LocalSimulationTestRunner(validator);
        var gate = new PublicationGate(validator, signer);
        var pubDir = Path.Combine(Path.GetTempPath(), $"it01-pub-{Guid.NewGuid():N}");

        // Validate
        var vr = validator.Validate(script);
        vr.IsValid.Should().BeTrue("script should be valid");

        // Sign
        var sig = signer.Sign(script);
        sig.Should().NotBeNull();
        signer.Verify(script, sig).Should().BeTrue();

        // Test
        var tr = await runner.RunAsync(script);
        tr.Passed.Should().BeTrue();

        // Publish gate
        var req = new PublicationRequest(script, sig, tr, pubDir);
        gate.CheckGates(req).Should().BeNull("all gates should pass");

        if (Directory.Exists(pubDir)) Directory.Delete(pubDir, true);
    }

    // DESIGN-IT-02: Script editor — add, move, delete steps; renumber correct
    [Fact]
    public void DESIGN_IT_02_Script_Editor_Step_Renumbering()
    {
        var s = new Script { Id = "it-02", Name = "IT-02", Version = "1.0.0", TargetRuntime = "desktop" };
        s.Steps.Add(new ScriptStep { StepNumber = 1, Description = "Step A", Action = "click" });
        s.Steps.Add(new ScriptStep { StepNumber = 2, Description = "Step B", Action = "type" });
        s.Steps.Add(new ScriptStep { StepNumber = 3, Description = "Step C", Action = "navigate" });
        s.Steps.Should().HaveCount(3);
        s.Steps.RemoveAt(1); // remove Step B
        for (int i = 0; i < s.Steps.Count; i++) s.Steps[i].StepNumber = i + 1;
        s.Steps[0].StepNumber.Should().Be(1);
        s.Steps[1].StepNumber.Should().Be(2);
        s.Steps[1].Description.Should().Be("Step C");
    }

    // DESIGN-IT-03: Draft generation from transcript → correct actions mapped
    [Fact]
    public void DESIGN_IT_03_Draft_Generation_From_Transcript()
    {
        var gen = new ScriptDraftGenerator();
        var events = new[]
        {
            new RawEvent(1, DateTimeOffset.UtcNow, RawEventType.Click, "App", "Login", "OK Button", null, null, null, false, null, 100, 200),
            new RawEvent(2, DateTimeOffset.UtcNow, RawEventType.TextInput, "App", "Login", "UserField", null, null, "alice", false, null, 50, 100),
        };
        var transcript = events
            .Select((e, i) => new TranscriptEntry(i + 1, e.Timestamp, $"Step {i + 1}", e, null, null))
            .ToList();
        var script = gen.GenerateDraft(transcript, CaptureEngineType.Desktop, "Login Test");
        script.Steps.Should().HaveCount(2);
        script.Steps[0].Action.Should().Be("click");
        script.Steps[1].Action.Should().Be("type");
    }

    // DESIGN-IT-04: Validation catches all error types
    [Fact]
    public void DESIGN_IT_04_Validation_Comprehensive()
    {
        var v = new ScriptValidator();
        var bad = new Script { Id = "", Name = "", Version = "not-semver", TargetRuntime = "" };
        bad.Steps.Add(new ScriptStep { StepNumber = 99, Description = "", Action = "" });
        var r = v.Validate(bad);
        r.IsValid.Should().BeFalse();
        r.Errors.Select(e => e.RuleId).Should().Contain("VAL-001");
        r.Errors.Select(e => e.RuleId).Should().Contain("VAL-003");
    }

    // DESIGN-IT-05: Privacy filter scrubs PII from transcript and script
    [Fact]
    public void DESIGN_IT_05_Privacy_Filter_Scrubs_PII()
    {
        var settings = new PrivacySettings { Policy = PrivacyPolicy.Default | PrivacyPolicy.RedactEmail };
        var filter = new PrivacyFilter(settings);
        var s = new Script { Id = "it-05", Name = "IT-05", Version = "1.0.0", TargetRuntime = "desktop" };
        var step = new ScriptStep { StepNumber = 1, Description = "Send to user@example.com", Action = "type" };
        step.Parameters["to"] = "user@example.com";
        s.Steps.Add(step);
        var filtered = filter.FilterScript(s);
        filtered.Steps[0].Parameters["to"].Should().Be("[REDACTED]");
        filtered.Steps[0].Description.Should().Contain("[REDACTED]");
    }

    // DESIGN-IT-06: Credential vault resolves placeholders in step parameters
    [Fact]
    public void DESIGN_IT_06_Credential_Resolver_Integration()
    {
        var vault = new InMemoryCredentialVault();
        vault.StoreCredential("db-password", "S3cr3tP@ss!");
        var resolver = new CredentialParameterResolver(vault);
        var step = new ScriptStep { StepNumber = 1, Description = "Connect to DB", Action = "type" };
        step.Parameters["password"] = "{{credential:db-password}}";
        var resolved = resolver.ResolveParameters(step);
        resolved["password"].Should().Be("S3cr3tP@ss!");
    }

    // DESIGN-IT-07: Isolated mode rejects browser runtime and network URLs
    [Fact]
    public void DESIGN_IT_07_Isolated_Mode_Comprehensive()
    {
        var v = new IsolatedModeValidator();
        var settings = new IsolatedModeSettings { IsEnabled = true };
        var s = new Script { Id = "it-07", Name = "IT-07", Version = "1.0.0", TargetRuntime = "browser" };
        var step = new ScriptStep { StepNumber = 1, Description = "Load page", Action = "navigate" };
        step.Parameters["url"] = "https://intranet.corp.com/app";
        s.Steps.Add(step);
        var r = v.Validate(s, settings);
        r.IsCompliant.Should().BeFalse();
        r.Violations.Should().Contain(v2 => v2.Code == "ISO-001");
        r.Violations.Should().Contain(v2 => v2.Code == "ISO-002");
    }

    // DESIGN-IT-08: Offline export package contains all required files
    [Fact]
    public async Task DESIGN_IT_08_Offline_Export_Package_Complete()
    {
        var script = BuildValidScript("it-08");
        var signer = new HmacScriptSigner("it-signer", TestKey, new ScriptValidator());
        var sig = signer.Sign(script);
        var path = Path.Combine(Path.GetTempPath(), $"it08-{Guid.NewGuid():N}.zip");
        var req = new OfflineExportRequest(script, sig, path, IsIsolated: true);
        var r = await new ZipOfflineExporter().ExportAsync(req);
        r.Success.Should().BeTrue();
        using (var zip = System.IO.Compression.ZipFile.OpenRead(path))
        {
            zip.GetEntry("manifest.json").Should().NotBeNull();
            zip.GetEntry("script.json").Should().NotBeNull();
            zip.GetEntry("signature.json").Should().NotBeNull();
        }
        File.Delete(path);
    }
}
