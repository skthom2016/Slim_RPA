namespace Tracto.Designer.Unit.Tests.Testing;

using FluentAssertions;
using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Core.Testing;
using Tracto.Designer.Core.Validation;

public class ScriptTestRunnerTests
{
    private static LocalSimulationTestRunner MakeRunner() => new(new ScriptValidator());

    private static Script ValidScript(int stepCount = 2)
    {
        var s = new Script { Id = "t", Name = "T", Version = "1.0.0", TargetRuntime = "desktop" };
        for (int i = 1; i <= stepCount; i++)
            s.Steps.Add(new ScriptStep { StepNumber = i, Description = $"Step {i}", Action = "click" });
        return s;
    }

    [Fact]
    public async Task Valid_Script_Passes()
    {
        var r = await MakeRunner().RunAsync(ValidScript());
        r.Passed.Should().BeTrue();
        r.StepResults.Should().HaveCount(2);
    }

    [Fact]
    public async Task Invalid_Script_Returns_Failure()
    {
        var s = new Script { Id = "", Name = "", Version = "", TargetRuntime = "" };
        var r = await MakeRunner().RunAsync(s);
        r.Passed.Should().BeFalse();
        r.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task All_Steps_Reported()
    {
        var r = await MakeRunner().RunAsync(ValidScript(3));
        r.StepResults.Should().HaveCount(3);
        r.StepResults.All(s => s.Passed).Should().BeTrue();
    }

    [Fact]
    public async Task Progress_Reported()
    {
        var reports = new List<int>();
        await MakeRunner().RunAsync(ValidScript(2), new Progress<int>(p => reports.Add(p)));
        reports.Should().Contain(100);
    }

    [Fact]
    public async Task Cancellation_Returns_Cancelled_Result()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var r = await MakeRunner().RunAsync(ValidScript(2), null, cts.Token);
        r.Passed.Should().BeFalse();
    }

    [Fact]
    public void ValidationFailure_Factory()
    {
        var r = ScriptTestResult.ValidationFailure("bad");
        r.Passed.Should().BeFalse();
        r.ErrorMessage.Should().Be("bad");
        r.StepResults.Should().BeEmpty();
    }

    [Fact]
    public void Name_Is_Local_Simulation()
    {
        MakeRunner().Name.Should().Be("Local Simulation");
    }
}
