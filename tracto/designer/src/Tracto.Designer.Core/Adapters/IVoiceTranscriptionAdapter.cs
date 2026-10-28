namespace Tracto.Designer.Core.Adapters;

public interface IVoiceTranscriptionAdapter
{
    bool IsAvailable { get; }
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    IAsyncEnumerable<(DateTimeOffset Timestamp, string Text)> GetTranscriptionsAsync(CancellationToken ct = default);
}
