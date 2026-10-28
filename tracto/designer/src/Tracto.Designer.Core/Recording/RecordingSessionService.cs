namespace Tracto.Designer.Core.Recording;

using Tracto.Designer.Core.Audit;

public sealed class RecordingSessionService : IRecordingSessionService
{
    private readonly object _lock = new();
    private SessionState _state = SessionState.Idle;
    private readonly List<TranscriptEntry> _transcript = [];
    private CaptureEngineType? _engineType;
    private string? _sessionId;
    private readonly AuditService? _audit;

    public RecordingSessionService() { }

    public RecordingSessionService(AuditService audit)
    {
        _audit = audit;
    }

    public SessionState CurrentState => _state;
    public CaptureEngineType? ActiveEngineType => _engineType;
    public string? SessionId => _sessionId;
    public IReadOnlyList<TranscriptEntry> Transcript => _transcript.AsReadOnly();
    public event EventHandler<SessionState>? StateChanged;

    public void Start(RecordingSessionOptions options)
    {
        SessionState newState;
        lock (_lock)
        {
            if (_state != SessionState.Idle)
                throw new InvalidOperationException($"Cannot Start from {_state}");
            _engineType = options.EngineType;
            _sessionId = options.ResolvedSessionId;
            _state = newState = SessionState.Recording;
        }
        StateChanged?.Invoke(this, newState);
        _audit?.LogRecordingStarted(_sessionId ?? "");
    }

    public void Pause()
    {
        SessionState newState;
        lock (_lock)
        {
            if (_state != SessionState.Recording)
                throw new InvalidOperationException($"Cannot Pause from {_state}");
            _state = newState = SessionState.Paused;
        }
        StateChanged?.Invoke(this, newState);
    }

    public void Resume()
    {
        SessionState newState;
        lock (_lock)
        {
            if (_state != SessionState.Paused)
                throw new InvalidOperationException($"Cannot Resume from {_state}");
            _state = newState = SessionState.Recording;
        }
        StateChanged?.Invoke(this, newState);
    }

    public void Stop()
    {
        SessionState newState;
        lock (_lock)
        {
            if (_state != SessionState.Recording && _state != SessionState.Paused)
                throw new InvalidOperationException($"Cannot Stop from {_state}");
            _state = newState = SessionState.Stopped;
        }
        StateChanged?.Invoke(this, newState);
        _audit?.LogRecordingStopped(_sessionId ?? "", _transcript.Count);
    }

    public void Discard()
    {
        SessionState newState;
        lock (_lock)
        {
            if (_state == SessionState.Idle) return;
            if (_state == SessionState.Stopped)
                throw new InvalidOperationException("Cannot Discard from Stopped");
            _transcript.Clear();
            _engineType = null;
            _sessionId = null;
            _state = newState = SessionState.Idle;
        }
        StateChanged?.Invoke(this, newState);
    }

    public void AppendTranscriptEntry(TranscriptEntry entry)
    {
        lock (_lock)
        {
            if (_state != SessionState.Recording && _state != SessionState.Paused) return;
            _transcript.Add(entry);
        }
    }
}
