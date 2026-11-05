namespace Tracto.Designer.Unit.Tests.Recording;

using FluentAssertions;
using Tracto.Designer.Core.Recording;
using Tracto.Designer.Core.Scripting;

/// <summary>
/// A recorded sentence has spaces in it. Dropping them silently split every recording into
/// word-sized type steps separated by key steps that carried no key at all, which the desktop
/// runtime rejects with CONFIGURATION at replay.
/// </summary>
public class RecordedTypingTests
{
    private static TranscriptEntry Key(int n, string key) =>
        new(n, DateTimeOffset.UnixEpoch.AddMilliseconds(n * 50), $"Key pressed: {key}",
            new RawEvent(n, DateTimeOffset.UnixEpoch.AddMilliseconds(n * 50), RawEventType.KeyPress,
                "notepad", "Untitled - Notepad", null, null, null, null, false, key, 0, 0),
            null, null);

    private static IReadOnlyList<TranscriptEntry> Typed(string text) =>
        [.. text.Select((c, i) => Key(i + 1, c.ToString()))];

    [Fact]
    public void A_Recorded_Sentence_Keeps_Its_Spaces_In_One_Type_Step()
    {
        var draft = new ScriptDraftGenerator()
            .GenerateDraft(Typed("hello world"), CaptureEngineType.Desktop, "Session");

        draft.Steps.Should().ContainSingle();
        draft.Steps[0].Action.Should().Be("type");
        draft.Steps[0].Parameters["value"].Should().Be("hello world");
    }

    [Fact]
    public void No_Recorded_Key_Step_Is_Left_Without_A_Key()
    {
        var entries = new List<TranscriptEntry> { Key(1, "a"), Key(2, " "), Key(3, "b"), Key(4, "Enter") };

        var draft = new ScriptDraftGenerator()
            .GenerateDraft(entries, CaptureEngineType.Desktop, "Session");

        draft.Steps.Should().Contain(s => s.Action == "key", "the Enter is still its own step");
        draft.Steps.Should().NotContain(s => s.Action == "key" && !s.Parameters.ContainsKey("key"),
            "the desktop runtime fails a key step that carries no key");
    }

    [Fact]
    public void Named_Keys_Still_Break_The_Typing_Into_Separate_Steps()
    {
        var entries = new List<TranscriptEntry> { Key(1, "h"), Key(2, "i"), Key(3, "Enter"), Key(4, "o"), Key(5, "k") };

        var draft = new ScriptDraftGenerator()
            .GenerateDraft(entries, CaptureEngineType.Desktop, "Session");

        draft.Steps.Select(s => s.Action).Should().Equal("type", "key", "type");
        draft.Steps[0].Parameters["value"].Should().Be("hi");
        draft.Steps[1].Parameters["key"].Should().Be("Enter");
        draft.Steps[2].Parameters["value"].Should().Be("ok");
    }
}
