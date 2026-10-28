namespace Tracto.Designer.Infrastructure.Adapters;

using System.Runtime.CompilerServices;
using Tracto.Designer.Core.Adapters;

public sealed class NullVoiceAdapter : IVoiceTranscriptionAdapter
{
    public bool IsAvailable => false;

    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

#pragma warning disable CS1998
    public async IAsyncEnumerable<(DateTimeOffset Timestamp, string Text)> GetTranscriptionsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield break;
    }
#pragma warning restore CS1998
}
