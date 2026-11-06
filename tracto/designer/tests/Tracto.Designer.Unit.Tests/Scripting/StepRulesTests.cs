namespace Tracto.Designer.Unit.Tests.Scripting;

using FluentAssertions;
using Tracto.Designer.App.ViewModels;
using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Core.Validation;

/// <summary>
/// Mistakes in a step that otherwise surface only when it runs: a missing parameter fails the
/// step, an unknown one is ignored in silence, and a step naming no element cannot resolve
/// anything at all.
/// </summary>
public class StepRulesTests
{
    private static ScriptStep Step(string action, params (string Key, string Value)[] p)
    {
        var step = new ScriptStep { StepNumber = 1, Action = action, Description = action };
        foreach (var (k, v) in p) step.Parameters[k] = v;
        return step;
    }

    private static IReadOnlyList<string> Rules(string runtime, ScriptStep step) =>
        [.. StepRules.Check(runtime, step).Select(i => i.RuleId)];

    [Fact]
    public void A_Missing_Required_Parameter_Is_An_Error()
    {
        var issues = StepRules.Check("browser", Step("fill", ("css", "#x")));

        issues.Should().Contain(i => i.RuleId == "VAL-032" && i.IsError);
        issues.Single(i => i.RuleId == "VAL-032").Message.Should().Contain("value");
    }

    [Fact]
    public void An_Empty_Required_Value_Is_An_Error()
    {
        Rules("browser", Step("navigate", ("url", "   "))).Should().Contain("VAL-033");
    }

    [Fact]
    public void An_Unknown_Parameter_Is_Reported_But_Does_Not_Stop_The_Run()
    {
        var issues = StepRules.Check("browser", Step("fill", ("css", "#x"), ("value", "hi"), ("labl", "To")));

        var unknown = issues.Single(i => i.RuleId == "VAL-034");
        unknown.IsError.Should().BeFalse();
        unknown.Message.Should().Contain("labl");
    }

    [Fact]
    public void A_Browser_Step_That_Names_No_Element_Is_An_Error()
    {
        var issues = StepRules.Check("browser", Step("click"));

        issues.Should().Contain(i => i.RuleId == "VAL-035" && i.IsError);
    }

    [Fact]
    public void Naming_The_Element_Twice_Is_Reported()
    {
        // The resolver tries them in order and uses the first that matches, so this wastes the
        // second rather than failing.
        var issues = StepRules.Check("browser", Step("click", ("label", "To"), ("css", "#to")));

        var many = issues.Single(i => i.RuleId == "VAL-036");
        many.IsError.Should().BeFalse();
        many.Message.Should().Contain("label");
    }

    [Fact]
    public void A_Role_Name_Counts_As_One_Way_Of_Naming_The_Element()
    {
        Rules("browser", Step("click", ("role", "button"), ("name", "Send")))
            .Should().NotContain("VAL-036", "name qualifies role rather than standing on its own");
    }

    [Fact]
    public void A_Name_Without_A_Role_Is_Reported()
    {
        Rules("browser", Step("click", ("css", "#x"), ("name", "Send"))).Should().Contain("VAL-037");
    }

    [Fact]
    public void Naming_The_Result_Of_An_Action_That_Produces_Nothing_Is_Reported()
    {
        var issues = StepRules.Check("browser", Step("fill", ("css", "#x"), ("value", "hi"), ("storeAs", "x")));

        var pointless = issues.Single(i => i.RuleId == "VAL-038");
        pointless.IsError.Should().BeFalse();
    }

    [Fact]
    public void A_Result_Name_On_An_Action_That_Produces_Data_Is_Fine()
    {
        Rules("browser", Step("getText", ("css", "h2"), ("storeAs", "headline")))
            .Should().NotContain("VAL-038");
    }

    [Fact]
    public void A_Number_That_Is_Not_A_Number_Is_An_Error()
    {
        Rules("desktop", Step("delay", ("ms", "soon"))).Should().Contain("VAL-039");
    }

    [Fact]
    public void A_Number_Still_To_Be_Filled_In_Is_Left_Alone()
    {
        Rules("desktop", Step("delay", ("ms", "${param.wait}")))
            .Should().NotContain("VAL-039", "the value arrives when the job runs");
    }

    [Fact]
    public void A_Desktop_Delay_Longer_Than_The_Runtime_Allows_Is_An_Error()
    {
        Rules("desktop", Step("delay", ("ms", "45000"))).Should().Contain("VAL-040");
        Rules("browser", Step("delay", ("ms", "45000")))
            .Should().NotContain("VAL-040", "only the desktop runtime caps a delay");
    }

    [Fact]
    public void An_Action_Outside_The_Catalogue_Is_Noted_But_Not_Picked_Apart()
    {
        var issues = StepRules.Check("desktop", Step("sheet.read", ("whatever", "x")));

        issues.Should().ContainSingle();
        issues[0].RuleId.Should().Be("VAL-031");
        issues[0].IsError.Should().BeFalse();
    }

    [Fact]
    public void An_Assert_On_The_Url_Needs_No_Element()
    {
        // The runtime only demands an element for the kinds that look at one.
        Rules("browser", Step("assert", ("kind", "urlEquals"), ("expected", "https://x/")))
            .Should().NotContain("VAL-035");
    }

    [Fact]
    public void Reading_Every_Match_Is_A_Known_Action()
    {
        StepRules.Check("browser", Step("getTexts", ("css", ".row"), ("storeAs", "rows")))
                 .Should().BeEmpty("getTexts reads every element the target matches");
    }

    [Fact]
    public void A_Correct_Step_Has_Nothing_To_Report()
    {
        StepRules.Check("desktop", Step("app.launch", ("executable", "notepad.exe"))).Should().BeEmpty();
        StepRules.Check("browser", Step("fill", ("label", "Subject"), ("value", "hi"))).Should().BeEmpty();
    }

    [Fact]
    public void Validate_Reports_Them_With_The_Step_Number()
    {
        var script = new Script { Id = "s", Name = "S", Version = "1.0.0", TargetRuntime = "desktop" };
        script.Steps.Add(Step("app.launch"));          // missing executable

        var result = new ScriptValidator().Validate(script);

        result.IsValid.Should().BeFalse();
        var error = result.Errors.Single(e => e.RuleId == "VAL-032");
        error.StepNumber.Should().Be(1);
        error.Message.Should().Contain("executable");
    }

    [Fact]
    public void The_Editor_Shows_A_Steps_Problems_Without_Waiting_For_Validate()
    {
        var script = new Script { Id = "s", Name = "S", Version = "1.0.0" };
        var segment = new ScriptSegment { Name = "Search", Runtime = "browser" };
        segment.Steps.Add(Step("fill", ("label", "To")));   // no value
        script.Segments.Add(segment);

        var vm = new ScriptEditorViewModel();
        vm.LoadScript(script);

        vm.StepRows[0].HasIssues.Should().BeTrue();
        vm.StepRows[0].IssueSummary.Should().Contain("value");
    }
}
