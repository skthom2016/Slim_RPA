namespace Tracto.Designer.Unit.Tests.Publication;

using System.IO;
using FluentAssertions;
using Tracto.Designer.App.ViewModels;
using Tracto.Designer.Core.Publication;
using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Core.Testing;
using Tracto.Designer.Core.Validation;

public class PublicationTests
{
    private static Script ValidScript()
    {
        var s = new Script { Id = "pub-test", Name = "PubTest", Version = "1.0.0", TargetRuntime = "desktop" };
        s.Steps.Add(new ScriptStep { StepNumber = 1, Description = "Click", Action = "click", Parameters = { ["coordinates"] = "10,10" } });
        return s;
    }

    private static ScriptTestResult PassedTest() =>
        new(true, [new StepTestResult(1, true, "OK", TimeSpan.FromMilliseconds(50))], TimeSpan.FromMilliseconds(50));

    private static ScriptTestResult FailedTest() =>
        new(false, [], TimeSpan.Zero, "simulated fail");

    [Fact]
    public async Task NullPublisher_Always_Succeeds()
    {
        var req = new PublicationRequest(ValidScript(), PassedTest(), "catalog");
        NullPublisher p = new();
        var result = await p.PublishAsync(req);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Gate_Blocks_Invalid_Script()
    {
        var invalid = new Script { Id = "", Name = "", Version = "", TargetRuntime = "" };
        var gate = new PublicationGate(new ScriptValidator());
        var req = new PublicationRequest(invalid, PassedTest(), "cat");
        gate.CheckGates(req).Should().NotBeNull();
        gate.CheckGates(req)!.Success.Should().BeFalse();
    }

    [Fact]
    public void Gate_Blocks_Failed_Test()
    {
        var gate = new PublicationGate(new ScriptValidator());
        var req = new PublicationRequest(ValidScript(), FailedTest(), "cat");
        gate.CheckGates(req)!.Success.Should().BeFalse();
    }

    [Fact]
    public void Gate_Passes_All_Good()
    {
        var gate = new PublicationGate(new ScriptValidator());
        var req = new PublicationRequest(ValidScript(), PassedTest(), "cat");
        gate.CheckGates(req).Should().BeNull();
    }

    // DS-16-AC01: when deployment policy does not mandate testing, a VALIDATED script
    // must be publishable without a passing test result.
    [Fact]
    public void Gate_Passes_When_Test_Not_Required_And_Test_Failed()
    {
        // testRequired = false → Gate 2 is skipped
        var gate = new PublicationGate(new ScriptValidator(), testRequired: false);
        var req = new PublicationRequest(ValidScript(), FailedTest(), "cat");
        gate.CheckGates(req).Should().BeNull("VALIDATED publish must succeed when policy does not mandate a test");
    }

    [Fact]
    public void Gate_Blocks_Failed_Test_When_Test_Required()
    {
        // Explicit testRequired = true — same semantics as the original default
        var gate = new PublicationGate(new ScriptValidator(), testRequired: true);
        var req = new PublicationRequest(ValidScript(), FailedTest(), "cat");
        gate.CheckGates(req)!.Success.Should().BeFalse();
    }

    [Fact]
    public void Gate_Passes_When_Test_Not_Required_And_No_Test_Run()
    {
        var gate = new PublicationGate(new ScriptValidator(), testRequired: false);
        var req = new PublicationRequest(ValidScript(), ScriptTestResult.NotRun, "cat");
        gate.CheckGates(req).Should().BeNull("VALIDATED publish with NotRun sentinel must succeed when test is not policy-required");
    }

    [Fact]
    public void PublicationResult_Failure_Factory()
    {
        var r = PublicationResult.Failure("test fail");
        r.Success.Should().BeFalse();
        r.Message.Should().Be("test fail");
    }

    [Fact]
    public void PublicationResult_Ok_Factory()
    {
        var r = PublicationResult.Ok("abc123");
        r.Success.Should().BeTrue();
        r.PublicationId.Should().Be("abc123");
        r.PublishedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task LocalFilePublisher_Publishes_To_Temp_Dir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tracto-pub-test-" + Guid.NewGuid().ToString("N"));
        var gate = new PublicationGate(new ScriptValidator());
        var pub = new Tracto.Designer.Infrastructure.Publication.LocalFilePublisher(dir, gate);
        var req = new PublicationRequest(ValidScript(), PassedTest(), dir);
        var r = await pub.PublishAsync(req);
        r.Success.Should().BeTrue();
        File.Exists(Path.Combine(dir, "catalog.json")).Should().BeTrue();
        File.ReadAllText(Path.Combine(dir, "pub-test_1.0.0.script.json")).Should().NotContain("signature");
        // cleanup
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task LocalFilePublisher_Blocks_Duplicate_Version()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tracto-pub-dup-" + Guid.NewGuid().ToString("N"));
        var gate = new PublicationGate(new ScriptValidator());
        var pub = new Tracto.Designer.Infrastructure.Publication.LocalFilePublisher(dir, gate);
        var req = new PublicationRequest(ValidScript(), PassedTest(), dir);
        await pub.PublishAsync(req); // first publish succeeds
        var r2 = await pub.PublishAsync(req); // second should fail
        r2.Success.Should().BeFalse();
        r2.Message.Should().Contain("already published");
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task Same_Open_Script_Publishes_Again_After_The_Version_Is_Bumped()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tracto-pub-bump-" + Guid.NewGuid().ToString("N"));
        var gate = new PublicationGate(new ScriptValidator(), testRequired: false);
        var publisher = new Tracto.Designer.Infrastructure.Publication.LocalFilePublisher(dir, gate);

        var editor = new ScriptEditorViewModel();
        var script = ValidScript();
        editor.LoadScript(script);
        var testing = new TestPanelViewModel(new LocalSimulationTestRunner(new ScriptValidator()), editor);
        var vm = new PublicationViewModel(publisher, editor, testing, testRequired: false) { OutputDirectory = dir };

        vm.PublishCommand.CanExecute(null).Should().BeTrue("an open script can be published without signing");
        vm.PublishCommand.Execute(null);
        await WaitUntilIdle(vm);
        vm.LastResult!.Success.Should().BeTrue();

        // Edit the same open script and bump its version, then publish again.
        editor.Script!.Steps[0].Description = "Click, edited";
        editor.Script.Version = "1.0.1";
        vm.PublishCommand.CanExecute(null).Should().BeTrue("editing the open script must not block another publish");
        vm.PublishCommand.Execute(null);
        await WaitUntilIdle(vm);

        vm.LastResult!.Success.Should().BeTrue(vm.LastResult.Message);
        File.Exists(Path.Combine(dir, "pub-test_1.0.1.script.json")).Should().BeTrue();
        Directory.Delete(dir, true);
    }

    private static async Task WaitUntilIdle(PublicationViewModel vm)
    {
        for (var i = 0; i < 200 && (vm.IsPublishing || !vm.HasResult); i++)
            await Task.Delay(25);
    }
}
