namespace Tracto.Designer.Unit.Tests.IsolatedMode;

using FluentAssertions;
using Tracto.Designer.Core.IsolatedMode;
using Tracto.Designer.Core.Scripting;

public class IsolatedModeTests
{
    private static readonly IsolatedModeValidator V = new();

    private static IsolatedModeSettings Enabled() => new() { IsEnabled = true };
    private static IsolatedModeSettings Disabled() => new() { IsEnabled = false };

    private static Script DesktopScript(params (string key, string val)[] parameters)
    {
        var s = new Script { Id = "t", Name = "T", Version = "1.0.0", TargetRuntime = "desktop" };
        var step = new ScriptStep { StepNumber = 1, Description = "D", Action = "click" };
        foreach (var (k, v) in parameters)
            step.Parameters[k] = v;
        s.Steps.Add(step);
        return s;
    }

    [Fact]
    public void Disabled_Mode_Always_Compliant()
    {
        var s = new Script { Id = "t", Name = "T", Version = "1.0.0", TargetRuntime = "browser" };
        V.Validate(s, Disabled()).IsCompliant.Should().BeTrue();
    }

    [Fact]
    public void Browser_Runtime_Violates_ISO001()
    {
        var s = new Script { Id = "t", Name = "T", Version = "1.0.0", TargetRuntime = "browser" };
        var r = V.Validate(s, Enabled());
        r.Violations.Should().Contain(v => v.Code == "ISO-001");
    }

    [Fact]
    public void Desktop_Runtime_No_ISO001()
    {
        var s = DesktopScript();
        V.Validate(s, Enabled()).Violations.Should().NotContain(v => v.Code == "ISO-001");
    }

    [Fact]
    public void Http_URL_In_Param_Violates_ISO002()
    {
        var s = DesktopScript(("url", "https://example.com/api"));
        V.Validate(s, Enabled()).Violations.Should().Contain(v => v.Code == "ISO-002");
    }

    [Fact]
    public void Local_Path_In_Param_Is_Compliant()
    {
        var s = DesktopScript(("path", @"C:\Reports\output.csv"));
        V.Validate(s, Enabled()).IsCompliant.Should().BeTrue();
    }

    [Fact]
    public void No_Network_Restrict_Skips_Url_Check()
    {
        var settings = new IsolatedModeSettings { IsEnabled = true, RestrictNetworkAccess = false };
        var s = DesktopScript(("url", "https://example.com"));
        V.Validate(s, settings).IsCompliant.Should().BeTrue();
    }

    [Fact]
    public void Compliant_Factory()
    {
        IsolatedValidationResult.Compliant().IsCompliant.Should().BeTrue();
    }

    [Fact]
    public void Violation_Record_Fields()
    {
        var v = new IsolatedViolation("ISO-001", "msg", 3);
        v.Code.Should().Be("ISO-001");
        v.StepNumber.Should().Be(3);
    }

    [Fact]
    public void UNC_Path_In_Param_Violates_ISO003()
    {
        var s = DesktopScript(("share", @"\\server\share\file.csv"));
        var r = V.Validate(s, Enabled());
        r.Violations.Should().Contain(v => v.Code == "ISO-003");
    }

    [Fact]
    public void UNC_Path_Forward_Slash_In_Param_Violates_ISO003()
    {
        var s = DesktopScript(("share", "//server/share/file.csv"));
        var r = V.Validate(s, Enabled());
        r.Violations.Should().Contain(v => v.Code == "ISO-003");
    }
}
