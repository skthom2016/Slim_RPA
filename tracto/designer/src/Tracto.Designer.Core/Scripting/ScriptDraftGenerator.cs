namespace Tracto.Designer.Core.Scripting;

using Tracto.Designer.Core.Recording;

public sealed class ScriptDraftGenerator : IScriptDraftGenerator
{
    public Script GenerateDraft(IReadOnlyList<TranscriptEntry> transcript, CaptureEngineType engineType, string sessionName)
    {
        var script = new Script
        {
            Name = string.IsNullOrWhiteSpace(sessionName) ? "Recorded Script" : sessionName,
            TargetRuntime = engineType switch
            {
                CaptureEngineType.Browser  => "browser",
                CaptureEngineType.Terminal => "terminal",
                _                          => "desktop"
            }
        };

        for (int i = 0; i < transcript.Count; i++)
        {
            var step = ConvertEntry(transcript[i], i + 1);
            script.Steps.Add(step);
        }

        return script;
    }

    private static ScriptStep ConvertEntry(TranscriptEntry entry, int stepNumber)
    {
        var raw = entry.SourceEvent;
        var action = MapAction(raw.EventType);
        var parameters = BuildParameters(raw);

        var description = entry.StepDescription;
        if (!string.IsNullOrWhiteSpace(entry.NarrationText))
            description += $" — [narrator: {entry.NarrationText}]";

        return new ScriptStep
        {
            StepNumber  = stepNumber,
            Description = description,
            Action      = action,
            Parameters  = parameters
        };
    }

    private static string MapAction(RawEventType t) => t switch
    {
        RawEventType.Click or RawEventType.DoubleClick or RawEventType.RightClick => "click",
        RawEventType.TextInput                                                      => "type",
        RawEventType.KeyPress or RawEventType.KeyDown or RawEventType.KeyUp        => "key",
        RawEventType.Scroll                                                          => "scroll",
        RawEventType.Focus                                                           => "focus",
        RawEventType.WindowActivate                                                  => "navigate",
        RawEventType.Drag or RawEventType.Drop                                      => "drag",
        _                                                                            => "action"
    };

    private static Dictionary<string, string?> BuildParameters(RawEvent raw)
    {
        var p = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(raw.ControlName))
            p["target"] = raw.ControlName;
        else if (!string.IsNullOrWhiteSpace(raw.AutomationId))
            p["target"] = raw.AutomationId;
        if (!string.IsNullOrWhiteSpace(raw.WindowTitle))
            p["window"] = raw.WindowTitle;
        if (!raw.IsPasswordField && !string.IsNullOrWhiteSpace(raw.Value))
            p["value"] = raw.Value;
        if (!string.IsNullOrWhiteSpace(raw.KeyValue))
            p["key"] = raw.KeyValue;
        if (raw.ScreenX != 0 || raw.ScreenY != 0)
            p["coordinates"] = $"{raw.ScreenX},{raw.ScreenY}";
        return p;
    }
}
