namespace Tracto.Designer.Core.Recording;

public sealed record TranscriptEntry(
    int StepNumber,
    DateTimeOffset Timestamp,
    string StepDescription,
    RawEvent SourceEvent,
    string? NarrationText,
    string? ScreenshotRelativePath);
