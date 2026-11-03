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

    /// <summary>
    /// Clears the transcript and returns to Idle from any state, so a new recording can start.
    /// Unlike <see cref="Discard"/> this is allowed once a session has been stopped.
    /// </summary>
    void Reset();

    void AppendTranscriptEntry(TranscriptEntry entry);
}
