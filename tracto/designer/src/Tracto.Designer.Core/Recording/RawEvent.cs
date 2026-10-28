namespace Tracto.Designer.Core.Recording;

public sealed record RawEvent(
    long Sequence,
    DateTimeOffset Timestamp,
    RawEventType EventType,
    string ProcessName,
    string WindowTitle,
    string? ControlName,
    string? AutomationId,
    string? ClassName,
    string? Value,
    bool IsPasswordField,
    string? KeyValue,
    int ScreenX,
    int ScreenY);
