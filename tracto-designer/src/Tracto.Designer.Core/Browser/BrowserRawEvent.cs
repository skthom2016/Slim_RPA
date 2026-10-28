namespace Tracto.Designer.Core.Browser;

public sealed record BrowserRawEvent(
    DateTimeOffset Timestamp,
    BrowserEventType EventType,
    string Url,
    string? ElementSelector,
    string? ElementText,
    string? InputValue,
    bool IsPasswordInput);
