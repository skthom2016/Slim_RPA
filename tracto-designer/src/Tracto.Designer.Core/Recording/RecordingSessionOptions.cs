namespace Tracto.Designer.Core.Recording;

public sealed record RecordingSessionOptions(
    CaptureEngineType EngineType,
    string OutputDirectory,
    string? SessionId = null)
{
    public string ResolvedSessionId => SessionId ?? Guid.NewGuid().ToString("N");
}
