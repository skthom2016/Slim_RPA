namespace Tracto.Designer.Core.Privacy;

using Tracto.Designer.Core.Recording;
using Tracto.Designer.Core.Scripting;

public interface IPrivacyFilter
{
    IEnumerable<TranscriptEntry> FilterTranscript(IEnumerable<TranscriptEntry> entries);
    Script FilterScript(Script script);
}
