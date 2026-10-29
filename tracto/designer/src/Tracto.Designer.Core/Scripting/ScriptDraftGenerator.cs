namespace Tracto.Designer.Core.Scripting;

using System.Text;
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

        var items = new List<DraftItem>();
        foreach (var entry in transcript)
        {
            if (IsNoise(entry.SourceEvent)) continue;

            var step = ConvertEntry(entry, 0);
            var typed = TypedCharacter(step);
            var last = items.Count > 0 ? items[^1] : null;

            if (typed is not null && last is { IsTyping: true })
            {
                last.Typed.Append(typed);
                last.End = entry.Timestamp;
                continue;
            }
            if (last is not null && IsRepeatableFocus(step) && SameStep(last.Step, step))
            {
                last.End = entry.Timestamp;
                continue;
            }

            items.Add(typed is null
                ? new DraftItem(step, entry.Timestamp)
                : DraftItem.Typing(step, typed, entry.Timestamp));
        }

        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0)
            {
                var gap = items[i].Start - items[i - 1].End;
                if (gap >= MinRecordedPause)
                    script.Steps.Add(DelayStep(gap));
            }
            script.Steps.Add(items[i].ToStep());
        }

        for (int i = 0; i < script.Steps.Count; i++)
            script.Steps[i].StepNumber = i + 1;

        return script;
    }

    private static readonly TimeSpan MinRecordedPause = TimeSpan.FromMilliseconds(500);
    private const int MaxRecordedPauseMs = 5000;

    // Windows of the Designer itself are never part of the automation being recorded.
    private static readonly string[] IgnoredWindows = ["Tracto Designer", "Input Capture Window"];

    private static bool IsNoise(RawEvent raw)
    {
        if (IgnoredWindows.Any(w => raw.WindowTitle.Contains(w, StringComparison.OrdinalIgnoreCase)))
            return true;
        if (raw.EventType is not (RawEventType.Focus or RawEventType.WindowActivate))
            return false;
        return string.IsNullOrWhiteSpace(raw.ControlName)
            && string.IsNullOrWhiteSpace(raw.AutomationId)
            && string.IsNullOrWhiteSpace(raw.WindowTitle);
    }

    private static bool IsRepeatableFocus(ScriptStep s) => s.Action is "focus" or "navigate";

    private static bool SameStep(ScriptStep a, ScriptStep b) =>
        a.Action == b.Action
        && a.Parameters.Count == b.Parameters.Count
        && a.Parameters.All(kv => b.Parameters.TryGetValue(kv.Key, out var v) && v == kv.Value);

    /// <summary>The character a key step types, or null for named keys and shortcuts.</summary>
    private static string? TypedCharacter(ScriptStep step)
    {
        if (step.Action != "key" || !step.Parameters.TryGetValue("key", out var key) || string.IsNullOrEmpty(key))
            return null;
        if (key.Length == 1) return key;
        // Recordings made before the recorder translated keys carry raw virtual-key codes.
        if (key.Length == 5 && key.StartsWith("VK_", StringComparison.Ordinal)
            && int.TryParse(key.AsSpan(3), System.Globalization.NumberStyles.HexNumber, null, out var vk))
        {
            if (vk is >= 0x30 and <= 0x39) return ((char)vk).ToString();
            if (vk is >= 0x41 and <= 0x5A) return ((char)(vk + 32)).ToString();
            if (vk == 0x20) return " ";
        }
        return null;
    }

    private static ScriptStep DelayStep(TimeSpan gap)
    {
        var ms = Math.Min(MaxRecordedPauseMs, (int)Math.Round(gap.TotalMilliseconds / 100) * 100);
        return new ScriptStep
        {
            Description = $"Wait {ms} ms",
            Action = "delay",
            Parameters = new Dictionary<string, string?> { ["ms"] = ms.ToString() }
        };
    }

    private sealed class DraftItem
    {
        private readonly ScriptStep _step;
        public DateTimeOffset Start { get; }
        public DateTimeOffset End { get; set; }
        public StringBuilder Typed { get; } = new();
        public bool IsTyping { get; private init; }
        public ScriptStep Step => _step;

        public DraftItem(ScriptStep step, DateTimeOffset at) { _step = step; Start = End = at; }

        public static DraftItem Typing(ScriptStep step, string first, DateTimeOffset at)
        {
            var item = new DraftItem(step, at) { IsTyping = true };
            item.Typed.Append(first);
            return item;
        }

        public ScriptStep ToStep()
        {
            if (!IsTyping) return _step;
            return new ScriptStep
            {
                Description = $"Type: {Typed}",
                Action = "type",
                Parameters = new Dictionary<string, string?> { ["value"] = Typed.ToString() }
            };
        }
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
        // Position inside the clicked window: replay uses this so a dialog that opens somewhere
        // else on screen is still clicked in the right place. Absolute coordinates stay as fallback.
        if (raw.WindowOffsetX is { } ox && raw.WindowOffsetY is { } oy && !string.IsNullOrWhiteSpace(raw.WindowTitle))
            p["offset"] = $"{ox},{oy}";
        return p;
    }
}
