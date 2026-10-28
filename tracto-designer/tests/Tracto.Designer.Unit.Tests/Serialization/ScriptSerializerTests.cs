namespace Tracto.Designer.Unit.Tests.Serialization;

using System.IO;
using FluentAssertions;
using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Infrastructure.Scripting;
using Tracto.Designer.Infrastructure.Serialization;

public class ScriptSerializerTests
{
    private static Script MakeScript()
    {
        var s = new Script
        {
            Id            = "ser-test",
            Name          = "Serializer Test",
            Version       = "2.0.1",
            TargetRuntime = "browser"
        };
        var step = new ScriptStep { StepNumber = 1, Description = "Navigate to login", Action = "navigate" };
        step.Parameters["url"] = "https://example.com";
        s.Steps.Add(step);
        s.Steps.Add(new ScriptStep { StepNumber = 2, Description = "Click button", Action = "click" });
        return s;
    }

    [Fact]
    public void Json_Roundtrip_Preserves_Fields()
    {
        var ser  = new JsonScriptSerializer();
        var s    = MakeScript();
        var json = ser.Serialize(s);
        var back = ser.Deserialize(json);

        back.Id.Should().Be(s.Id);
        back.Name.Should().Be(s.Name);
        back.Version.Should().Be(s.Version);
        back.TargetRuntime.Should().Be(s.TargetRuntime);
        back.Steps.Should().HaveCount(2);
        back.Steps[0].Parameters["url"].Should().Be("https://example.com");
    }

    [Fact]
    public void Json_Serialize_Is_Valid_Json()
    {
        var json = new JsonScriptSerializer().Serialize(MakeScript());
        var act  = () => System.Text.Json.JsonDocument.Parse(json);
        act.Should().NotThrow();
    }

    [Fact]
    public void Yaml_Roundtrip_Preserves_Core_Fields()
    {
        var ser  = new YamlScriptSerializer();
        var s    = MakeScript();
        var yaml = ser.Serialize(s);
        var back = ser.Deserialize(yaml);

        back.Id.Should().Be(s.Id);
        back.Name.Should().Be(s.Name);
        back.Version.Should().Be(s.Version);
        back.TargetRuntime.Should().Be(s.TargetRuntime);
    }

    // DS-12: steps and parameter values must survive a YAML roundtrip losslessly.
    [Fact]
    public void Yaml_Roundtrip_Preserves_Steps_And_Parameters()
    {
        var ser  = new YamlScriptSerializer();
        var s    = MakeScript();
        var yaml = ser.Serialize(s);
        var back = ser.Deserialize(yaml);

        back.Steps.Should().HaveCount(s.Steps.Count, "all steps must survive the YAML roundtrip");
        var step0 = back.Steps[0];
        step0.StepNumber.Should().Be(s.Steps[0].StepNumber);
        step0.Action.Should().Be(s.Steps[0].Action);
        step0.Description.Should().Be(s.Steps[0].Description);
        step0.Parameters.Should().ContainKey("url").WhoseValue.Should().Be("https://example.com",
            "parameter values must be preserved through YAML serialization");
        back.Steps[1].Action.Should().Be(s.Steps[1].Action);
    }

    [Fact]
    public void Yaml_Contains_Expected_Keys()
    {
        var yaml = new YamlScriptSerializer().Serialize(MakeScript());
        yaml.Should().Contain("id: ");
        yaml.Should().Contain("steps:");
        yaml.Should().Contain("action: ");
    }

    [Fact]
    public void Json_Empty_Script_Roundtrip()
    {
        var ser = new JsonScriptSerializer();
        var s   = new Script { Id = "e", Name = "E", Version = "1.0.0", TargetRuntime = "desktop" };
        var back = ser.Deserialize(ser.Serialize(s));
        back.Steps.Should().BeEmpty();
    }

    [Fact]
    public void Json_Null_Parameter_Value_Preserved()
    {
        var ser  = new JsonScriptSerializer();
        var s    = new Script { Id = "n", Name = "N", Version = "1.0.0", TargetRuntime = "desktop" };
        var step = new ScriptStep { StepNumber = 1, Description = "x", Action = "y" };
        step.Parameters["key"] = null;
        s.Steps.Add(step);
        var back = ser.Deserialize(ser.Serialize(s));
        back.Steps[0].Parameters["key"].Should().BeNull();
    }

    [Fact]
    public async Task FileScriptRepository_SaveLoad_Roundtrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tracto-repo-test-{Guid.NewGuid():N}.json");
        var repo = new FileScriptRepository();
        var s    = MakeScript();
        await repo.SaveAsync(s, path);
        var back = await repo.LoadAsync(path);
        back.Id.Should().Be(s.Id);
        back.Steps.Should().HaveCount(2);
        File.Delete(path);
    }
}
