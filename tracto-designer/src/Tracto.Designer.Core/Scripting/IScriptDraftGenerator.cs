namespace Tracto.Designer.Core.Scripting;

using Tracto.Designer.Core.Recording;

public interface IScriptDraftGenerator
{
    Script GenerateDraft(IReadOnlyList<TranscriptEntry> transcript, CaptureEngineType engineType, string sessionName);
}
