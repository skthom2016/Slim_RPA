namespace Tracto.Designer.Core.Recording;

public interface IRecordingSessionService
{
    SessionState CurrentState { get; }
    CaptureEngineType? ActiveEngineType { get; }
    string? SessionId { get; }
    IReadOnlyList<TranscriptEntry> Transcript { get; }
    event EventHandler<SessionState> StateChanged;
    void Start(RecordingSessionOptions options);
    void Pause();
    void Resume();
    void Stop();
    void Discard();
    void AppendTranscriptEntry(TranscriptEntry entry);
}
