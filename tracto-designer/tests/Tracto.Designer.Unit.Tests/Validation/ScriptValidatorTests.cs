namespace Tracto.Designer.Unit.Tests.Validation;

using FluentAssertions;
using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Core.Validation;

public class ScriptValidatorTests
{
    private static readonly ScriptValidator V = new();

    private static Script ValidScript()
    {
        var s = new Script { Id = "test", Name = "Test Script", Version = "1.0.0", TargetRuntime = "desktop" };
        s.Steps.Add(new ScriptStep { StepNumber = 1, Description = "Click OK", Action = "click" });
        return s;
    }

    [Fact]
    public void Valid_Script_Passes()
    {
        V.Validate(ValidScript()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_Id_Is_Error()
    {
        var s = ValidScript(); s.Id = "";
        V.Validate(s).Errors.Should().Contain(e => e.RuleId == "VAL-001" && e.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Empty_Name_Is_Error()
    {
        var s = ValidScript(); s.Name = "";
        V.Validate(s).Errors.Should().Contain(e => e.RuleId == "VAL-002");
    }

    [Fact]
    public void Invalid_Version_Is_Error()
    {
        var s = ValidScript(); s.Version = "not-semver";
        V.Validate(s).Errors.Should().Contain(e => e.RuleId == "VAL-003");
    }

    [Theory]
    [InlineData("1.0.0.4")]
    [InlineData("1.0.0-SNAPSHOT")]
    [InlineData("1.0.0RC1")]
    public void Version_With_Trailing_Garbage_Is_Error(string badVersion)
    {
        var s = ValidScript(); s.Version = badVersion;
        V.Validate(s).Errors.Should().Contain(e => e.RuleId == "VAL-003",
            $"'{badVersion}' must not be accepted as valid semver");
    }

    [Fact]
    public void Empty_TargetRuntime_Is_Error()
    {
        var s = ValidScript(); s.TargetRuntime = "";
        V.Validate(s).Errors.Should().Contain(e => e.RuleId == "VAL-004");
    }

    [Fact]
    public void Zero_Steps_Is_Warning_Not_Error()
    {
        var s = new Script { Id = "x", Name = "X", Version = "1.0.0", TargetRuntime = "desktop" };
        var r = V.Validate(s);
        r.IsValid.Should().BeTrue();
        r.HasWarnings.Should().BeTrue();
        r.Errors.Should().Contain(e => e.RuleId == "VAL-005" && e.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void Empty_Step_Description_Is_Error()
    {
        var s = ValidScript(); s.Steps[0].Description = "";
        V.Validate(s).Errors.Should().Contain(e => e.RuleId == "VAL-011" && e.StepNumber == 1);
    }

    [Fact]
    public void Empty_Step_Action_Is_Error()
    {
        var s = ValidScript(); s.Steps[0].Action = "";
        V.Validate(s).Errors.Should().Contain(e => e.RuleId == "VAL-012");
    }

    [Fact]
    public void Misnumbered_Step_Is_Error()
    {
        var s = ValidScript(); s.Steps[0].StepNumber = 99;
        V.Validate(s).Errors.Should().Contain(e => e.RuleId == "VAL-010");
    }

    [Fact]
    public void Multiple_Steps_All_Valid()
    {
        var s = new Script { Id = "x", Name = "X", Version = "1.0.0", TargetRuntime = "desktop" };
        s.Steps.Add(new ScriptStep { StepNumber = 1, Description = "Click A", Action = "click" });
        s.Steps.Add(new ScriptStep { StepNumber = 2, Description = "Type B", Action = "type" });
        V.Validate(s).IsValid.Should().BeTrue();
    }
}
