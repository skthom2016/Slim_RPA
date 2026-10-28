namespace Tracto.Designer.Infrastructure.Adapters;

using Tracto.Designer.Core.Adapters;
using Tracto.Designer.Core.Recording;

public sealed class NullAiAdapter : IAiAuthoringAdapter
{
    public bool IsAvailable => false;

    public Task<string?> SuggestStepDescriptionAsync(RawEvent rawEvent, IReadOnlyList<TranscriptEntry> priorSteps, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    public Task<IReadOnlyList<TranscriptEntry>> EnhanceDraftAsync(IReadOnlyList<TranscriptEntry> draft, CancellationToken ct = default)
        => Task.FromResult(draft);
}
