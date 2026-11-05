namespace Tracto.Designer.Unit.Tests.Scripting;

using FluentAssertions;
using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Core.Validation;

/// <summary>
/// Data crosses a segment boundary one way: a step names its outputs with storeAs, the
/// Orchestrator copies a finished segment's outputs into the parameters of the segments after
/// it, and a later step reads them as ${param.name.field}.
/// </summary>
public class DataFlowTests
{
    private static ScriptStep Step(int n, string action, params (string Key, string Value)[] p)
    {
        var step = new ScriptStep { StepNumber = n, Action = action, Description = action };
        foreach (var (k, v) in p) step.Parameters[k] = v;
        return step;
    }

    /// <summary>A browser segment that reads a heading, then a desktop segment that types it.</summary>
    private static Script BingToNotepad()
    {
        var script = new Script { Id = "bing-to-notepad", Name = "Bing To Notepad", Version = "1.0.0" };
        script.Params["query"] = "Wonderla";

        var browser = new ScriptSegment { Name = "Search", Runtime = "browser" };
        browser.Steps.Add(Step(1, "fill", ("placeholder", "Search the web"), ("value", "${param.query}")));
        browser.Steps.Add(Step(2, "getText", ("css", "h2"), ("storeAs", "headline")));

        var desktop = new ScriptSegment { Name = "Write it down", Runtime = "desktop" };
        desktop.Steps.Add(Step(1, "notepad.type", ("text", "Found: ${param.headline.value}")));

        script.Segments.Add(browser);
        script.Segments.Add(desktop);
        return script;
    }

    [Fact]
    public void A_Later_Segment_Can_Read_What_An_Earlier_One_Stored()
    {
        var script = BingToNotepad();

        var available = DataFlow.AvailableTo(script, script.Segments[1], script.Segments[1].Steps[0]);

        available.Select(d => d.Reference).Should().Contain("${param.headline.value}");
        available.Single(d => d.Reference == "${param.headline.value}").Origin
                 .Should().Be(DataOrigin.EarlierSegment);
        available.Single(d => d.Reference == "${param.headline.value}").ProducedBy
                 .Should().Be("Segment 1 (Search), step 2");
    }

    [Fact]
    public void Script_Parameters_Are_Offered_To_Every_Segment()
    {
        var script = BingToNotepad();

        foreach (var segment in script.Segments)
            DataFlow.AvailableTo(script, segment).Select(d => d.Reference)
                    .Should().Contain("${param.query}", "a job can override it for any segment");
    }

    [Fact]
    public void An_Earlier_Segment_Cannot_Read_A_Later_One()
    {
        var script = BingToNotepad();

        DataFlow.AvailableTo(script, script.Segments[0], script.Segments[0].Steps[0])
                .Select(d => d.Reference)
                .Should().NotContain("${param.headline.value}", "outputs only flow forward");
    }

    [Fact]
    public void Inside_One_Segment_A_Stored_Value_Needs_No_Param_Prefix()
    {
        var script = new Script { Id = "s", Name = "S", Version = "1.0.0", TargetRuntime = "desktop" };
        script.Steps.Add(Step(1, "file.read", ("path", "C:/in.txt"), ("storeAs", "note")));
        script.Steps.Add(Step(2, "type", ("value", "x")));

        var available = DataFlow.AvailableTo(script, null, script.Steps[1]);

        available.Select(d => d.Reference).Should().Contain("${note.firstLine}");
        available.Single(d => d.Reference == "${note.firstLine}").Origin.Should().Be(DataOrigin.EarlierStep);
    }

    [Fact]
    public void A_Step_Only_Sees_The_Steps_Before_It()
    {
        var script = new Script { Id = "s", Name = "S", Version = "1.0.0", TargetRuntime = "desktop" };
        script.Steps.Add(Step(1, "type", ("value", "x")));
        script.Steps.Add(Step(2, "file.read", ("path", "C:/in.txt"), ("storeAs", "note")));

        DataFlow.AvailableTo(script, null, script.Steps[0]).Select(d => d.Reference)
                .Should().NotContain("${note.text}");
    }

    [Fact]
    public void A_Reference_Nothing_Produces_Is_Reported()
    {
        var script = BingToNotepad();
        script.Segments[1].Steps[0].Parameters["text"] = "Found: ${param.headlime.value}";

        DataFlow.UnresolvedIn(script, script.Segments[1], script.Segments[1].Steps[0])
                .Should().Equal("param.headlime.value");
    }

    [Fact]
    public void Built_In_And_Secret_References_Always_Resolve()
    {
        var script = BingToNotepad();
        script.Segments[1].Steps[0].Parameters["text"] = "${now.local} ${secret:API_KEY}";

        DataFlow.UnresolvedIn(script, script.Segments[1], script.Segments[1].Steps[0]).Should().BeEmpty();
    }

    [Fact]
    public void An_Action_Outside_The_Catalogue_Has_Its_Fields_Trusted()
    {
        var script = new Script { Id = "s", Name = "S", Version = "1.0.0", TargetRuntime = "desktop" };
        script.Steps.Add(Step(1, "sheet.read", ("storeAs", "rows")));
        script.Steps.Add(Step(2, "type", ("value", "${rows.whateverTheHandlerReturns}")));

        DataFlow.UnresolvedIn(script, null, script.Steps[1])
                .Should().BeEmpty("we cannot know the outputs of an action we do not ship");
    }

    [Fact]
    public void Validation_Fails_A_Script_Whose_Data_Flow_Is_Broken()
    {
        var script = BingToNotepad();
        script.Segments[1].Steps[0].Parameters["text"] = "Found: ${param.nothing.value}";

        var result = new ScriptValidator().Validate(script);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.RuleId == "VAL-030" && e.Message.Contains("param.nothing.value"));
    }

    [Fact]
    public void Validation_Passes_The_Flow_That_Actually_Works()
    {
        new ScriptValidator().Validate(BingToNotepad()).Errors
            .Should().NotContain(e => e.RuleId == "VAL-030");
    }
}
