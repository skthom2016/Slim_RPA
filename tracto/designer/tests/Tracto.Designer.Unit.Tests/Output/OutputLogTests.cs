namespace Tracto.Designer.Unit.Tests.Output;

using FluentAssertions;
using Moq;
using Tracto.Designer.App.ViewModels;
using Tracto.Designer.Core.Publication;
using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Core.Signing;
using Tracto.Designer.Core.Testing;
using Tracto.Designer.Core.Validation;

/// <summary>
/// Checking, signing and publishing used to each report into a panel of their own, so pressing a
/// toolbar button left its result on a tab the author was not looking at. They now append to one
/// output list in the order they happened.
/// </summary>
public class OutputLogTests
{
    private static ScriptEditorViewModel EditorWithScript()
    {
        var editor = new ScriptEditorViewModel();
        editor.LoadScript(new Script { Id = "demo", Name = "Demo", Version = "1.0.0", TargetRuntime = "desktop" });
        return editor;
    }

    [Fact]
    public void Validation_Reports_Every_Problem_And_A_Verdict()
    {
        var log = new OutputLogViewModel();
        var validator = new Mock<IScriptValidator>();
        validator.Setup(v => v.Validate(It.IsAny<Script>())).Returns(new ScriptValidationResult(
        [
            new ValidationError("STEP-001", ValidationSeverity.Error, "Unknown action", 2),
            new ValidationError("STEP-009", ValidationSeverity.Warning, "No description")
        ]));

        new ValidationViewModel(validator.Object, EditorWithScript(), log).ValidateCommand.Execute(null);

        log.Entries.Should().HaveCount(3);
        log.Entries[0].Message.Should().Be("STEP-001: Unknown action (step 2)");
        log.Entries[0].Severity.Should().Be(OutputSeverity.Error);
        log.Entries[1].Message.Should().Be("STEP-009: No description");
        log.Entries[2].Message.Should().Be("Invalid — 1 error, 1 warning.");
        log.Entries.Should().OnlyContain(e => e.Source == "Validation");
    }

    [Fact]
    public void A_Clean_Validation_Says_So()
    {
        var log = new OutputLogViewModel();
        var validator = new Mock<IScriptValidator>();
        validator.Setup(v => v.Validate(It.IsAny<Script>())).Returns(ScriptValidationResult.Pass());

        new ValidationViewModel(validator.Object, EditorWithScript(), log).ValidateCommand.Execute(null);

        log.Entries.Should().ContainSingle();
        log.Entries[0].Severity.Should().Be(OutputSeverity.Success);
        log.Entries[0].Message.Should().Be("Valid — no problems found.");
    }

    [Fact]
    public async Task A_Test_Run_Reports_Its_Steps_And_Its_Verdict()
    {
        var log = new OutputLogViewModel();
        var runner = new Mock<IScriptTestRunner>();
        runner.SetupGet(r => r.Name).Returns("Local simulation");
        runner.Setup(r => r.RunAsync(It.IsAny<Script>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ScriptTestResult(true,
                  [new StepTestResult(1, true, "Launched Notepad", TimeSpan.FromMilliseconds(120))],
                  TimeSpan.FromSeconds(2)));

        var vm = new TestPanelViewModel(runner.Object, EditorWithScript(), log);
        vm.RunTestCommand.Execute(null);
        await WaitUntil(() => !vm.IsRunning);

        log.Entries[0].Message.Should().StartWith("Running Demo with Local simulation");
        log.Entries[1].Message.Should().Be("Step 1: Launched Notepad (120 ms)");
        log.Entries[^1].Severity.Should().Be(OutputSeverity.Success);
        log.Entries[^1].Message.Should().Be("PASSED — 1 step in 2.0 s.");
    }

    [Fact]
    public void Signing_And_Verifying_Both_Report()
    {
        var log = new OutputLogViewModel();
        var signature = new ScriptSignature("author@example.com", "RS256", DateTimeOffset.UnixEpoch, new string('a', 64));
        var signer = new Mock<IScriptSigner>();
        signer.SetupGet(s => s.SignerId).Returns("author@example.com");
        signer.Setup(s => s.Sign(It.IsAny<Script>())).Returns(signature);
        signer.Setup(s => s.Verify(It.IsAny<Script>(), signature)).Returns(true);

        var vm = new SigningViewModel(signer.Object, EditorWithScript(), log);
        vm.SignCommand.Execute(null);
        vm.VerifyCommand.Execute(null);

        log.Entries.Should().HaveCount(2);
        log.Entries[0].Message.Should().Contain("RS256").And.Contain("aaaaaaaaaaaaaaaa...");
        log.Entries[1].Message.Should().Be("Signature valid.");
        log.Entries.Should().OnlyContain(e => e.Severity == OutputSeverity.Success);
    }

    [Fact]
    public async Task Publishing_Reports_Where_It_Went_And_How_It_Went()
    {
        var log = new OutputLogViewModel();
        var editor = EditorWithScript();
        var signer = new Mock<IScriptSigner>();
        signer.SetupGet(s => s.SignerId).Returns("author");
        signer.Setup(s => s.Sign(It.IsAny<Script>()))
              .Returns(new ScriptSignature("author", "RS256", DateTimeOffset.UnixEpoch, "abc"));
        var signing = new SigningViewModel(signer.Object, editor, log);
        signing.SignCommand.Execute(null);

        var runner = new Mock<IScriptTestRunner>();
        runner.SetupGet(r => r.Name).Returns("runner");
        var testing = new TestPanelViewModel(runner.Object, editor, log);

        var publisher = new Mock<IScriptPublisher>();
        publisher.SetupGet(p => p.Name).Returns("Local publisher");
        publisher.Setup(p => p.PublishAsync(It.IsAny<PublicationRequest>()))
                 .ReturnsAsync(PublicationResult.Ok("PUB-7"));

        var vm = new PublicationViewModel(publisher.Object, editor, signing, testing,
            testRequired: false, output: log);
        vm.OutputDirectory = @"C:\out";
        vm.PublishCommand.Execute(null);
        await WaitUntil(() => !vm.IsPublishing);

        log.Entries[^2].Message.Should().Be(@"Publishing Demo to C:\out with Local publisher...");
        log.Entries[^1].Message.Should().Be("Published — ID PUB-7");
        log.Entries[^1].Severity.Should().Be(OutputSeverity.Success);
    }

    [Fact]
    public void Results_From_Every_Source_Land_In_One_List_In_Order()
    {
        var log = new OutputLogViewModel();
        var editor = EditorWithScript();

        var validator = new Mock<IScriptValidator>();
        validator.Setup(v => v.Validate(It.IsAny<Script>())).Returns(ScriptValidationResult.Pass());
        new ValidationViewModel(validator.Object, editor, log).ValidateCommand.Execute(null);

        var signer = new Mock<IScriptSigner>();
        signer.SetupGet(s => s.SignerId).Returns("author");
        signer.Setup(s => s.Sign(It.IsAny<Script>()))
              .Returns(new ScriptSignature("author", "RS256", DateTimeOffset.UnixEpoch, "abc"));
        new SigningViewModel(signer.Object, editor, log).SignCommand.Execute(null);

        log.Entries.Select(e => e.Source).Should().Equal("Validation", "Signing");
    }

    [Fact]
    public void Clearing_Empties_The_Output()
    {
        var log = new OutputLogViewModel();
        log.Info("Test", "something happened");

        log.IsEmpty.Should().BeFalse();
        log.ClearCommand.CanExecute(null).Should().BeTrue();

        log.ClearCommand.Execute(null);

        log.Entries.Should().BeEmpty();
        log.IsEmpty.Should().BeTrue();
        log.ClearCommand.CanExecute(null).Should().BeFalse("there is nothing left to clear");
    }

    [Fact]
    public void A_Long_Session_Keeps_Only_The_Most_Recent_Lines()
    {
        var log = new OutputLogViewModel();
        for (int i = 0; i < OutputLogViewModel.MaxEntries + 20; i++)
            log.Info("Test", $"line {i}");

        log.Entries.Should().HaveCount(OutputLogViewModel.MaxEntries);
        log.Entries[^1].Message.Should().Be($"line {OutputLogViewModel.MaxEntries + 19}");
        log.Entries[0].Message.Should().Be("line 20", "the oldest lines are the ones dropped");
    }

    private static async Task WaitUntil(Func<bool> done)
    {
        for (int i = 0; i < 100 && !done(); i++) await Task.Delay(20);
        done().Should().BeTrue("the operation should have finished");
    }
}
