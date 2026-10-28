namespace Tracto.Designer.Infrastructure.Adapters;

using Tracto.Designer.Core.Adapters;

public sealed class NullScreenshotAdapter : IScreenshotAdapter
{
    public bool IsAvailable => false;

    public Task<string?> CaptureAsync(string sessionId, int stepNumber, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}
