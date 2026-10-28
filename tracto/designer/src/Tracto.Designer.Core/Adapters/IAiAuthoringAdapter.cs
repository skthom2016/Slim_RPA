namespace Tracto.Designer.Core.Adapters;

using Tracto.Designer.Core.Recording;

public interface IAiAuthoringAdapter
{
    bool IsAvailable { get; }
    Task<string?> SuggestStepDescriptionAsync(RawEvent rawEvent, IReadOnlyList<TranscriptEntry> priorSteps, CancellationToken ct = default);
    Task<IReadOnlyList<TranscriptEntry>> EnhanceDraftAsync(IReadOnlyList<TranscriptEntry> draft, CancellationToken ct = default);
}
