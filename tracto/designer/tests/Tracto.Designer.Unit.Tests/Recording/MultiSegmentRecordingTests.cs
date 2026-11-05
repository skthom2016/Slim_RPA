namespace Tracto.Designer.Unit.Tests.Recording;

using FluentAssertions;
using Moq;
using Tracto.Designer.App.ViewModels;
using Tracto.Designer.Core.Adapters;
using Tracto.Designer.Core.Audit;
using Tracto.Designer.Core.Browser;
using Tracto.Designer.Core.Credentials;
using Tracto.Designer.Core.Desktop;
using Tracto.Designer.Core.IsolatedMode;
using Tracto.Designer.Core.Mcp;
using Tracto.Designer.Core.OfflineExport;
using Tracto.Designer.Core.Privacy;
using Tracto.Designer.Core.Publication;
using Tracto.Designer.Core.Recording;
using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Core.Signing;
using Tracto.Designer.Core.Templates;
using Tracto.Designer.Core.Testing;
using Tracto.Designer.Core.Validation;

/// <summary>
/// A segmented script is recorded one segment at a time. Leaving the session Stopped after the
/// first recording disabled Start for the rest of the session, so the only way to record a
/// second segment was New Script — which throws the first segment away.
/// </summary>
public class MultiSegmentRecordingTests
{
    private static RecordingViewModel Build(out IRecordingSessionService session) =>
        TestRecording.BuildViewModel(out session);

    [Fact]
    public void A_Second_Segment_Can_Be_Recorded_Without_Discarding_The_First()
    {
        var vm = Build(out var session);

        vm.StartCommand.Execute(null);
        vm.StopCommand.Execute(null);

        vm.IsStopped.Should().BeTrue();
        vm.StartCommand.CanExecute(null).Should().BeTrue(
            "the next segment has to be recordable without starting the script again");

        vm.StartCommand.Execute(null);

        vm.IsRecording.Should().BeTrue("a second recording actually starts");
        session.Transcript.Should().BeEmpty("the new recording starts from nothing, not the last one's events");
    }

    [Fact]
    public void Recording_A_Second_Segment_Keeps_The_Segments_Already_In_The_Editor()
    {
        var vm = Build(out _);
        vm.ScriptEditor.AddSegmentCommand.Execute(null);
        var first = vm.ScriptEditor.SelectedSegment;
        first.Should().NotBeNull();

        vm.StartCommand.Execute(null);
        vm.StopCommand.Execute(null);
        vm.ScriptEditor.AddSegmentCommand.Execute(null);
        vm.StartCommand.Execute(null);

        vm.ScriptEditor.Script!.Segments.Should().HaveCount(2);
        vm.ScriptEditor.Script.Segments[0].Should().BeSameAs(first);
    }
}
