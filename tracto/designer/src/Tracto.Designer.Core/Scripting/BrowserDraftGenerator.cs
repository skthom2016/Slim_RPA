namespace Tracto.Designer.Core.Scripting;

using System.Text.RegularExpressions;
using Tracto.Designer.Core.Browser;
using Tracto.Designer.Core.Recording;

/// <summary>
/// Builds a browser script from a recorded transcript using the Browser Runtime's actions:
/// <c>navigate</c>, <c>fill</c>, <c>click</c>, <c>press</c>, <c>check</c>/<c>uncheck</c>,
/// <c>select</c> and <c>delay</c>. Passwords are never written into the script; they become
/// <c>${secret:NAME}</c> references resolved on the worker.
/// </summary>
public static class BrowserDraftGenerator
{
    private static readonly TimeSpan MinRecordedPause = TimeSpan.FromMilliseconds(500);
    private const int MaxRecordedPauseMs = 5000;

    // A navigation that happens this soon after a click or key press was caused by it.
    private static readonly TimeSpan CausedNavigationWindow = TimeSpan.FromSeconds(5);

    public static Script Generate(IReadOnlyList<TranscriptEntry> transcript, string sessionName)
    {
        var script = new Script
        {
            Name = string.IsNullOrWhiteSpace(sessionName) ? "Recorded Browser Script" : sessionName,
            TargetRuntime = "browser"
        };

        var steps = new List<(ScriptStep Step, DateTimeOffset At)>();
        DateTimeOffset? lastActionAt = null;
        string? lastUrl = null;

        foreach (var entry in transcript)
        {
            var raw = entry.SourceEvent;
            var target = BrowserTarget.FromAttributes(raw.Attributes)
                         ?? new BrowserTarget(Text: raw.ControlName, Css: raw.AutomationId);

            ScriptStep? step = raw.EventType switch
            {
                RawEventType.WindowActivate => NavigateStep(raw, ref lastUrl, lastActionAt, steps.Count),
                RawEventType.Click or RawEventType.DoubleClick => ClickStep(raw, target),
                RawEventType.TextInput => InputStep(raw, target),
                RawEventType.KeyPress or RawEventType.KeyDown => PressStep(raw, target),
                _ => null
            };

            if (step is null) continue;

            if (!string.IsNullOrWhiteSpace(entry.NarrationText))
                step.Description += $" — [narrator: {entry.NarrationText}]";

            // A click on a text field followed by typing into it is just the fill.
            if (step.Action == "fill" && steps.Count > 0 &&
                steps[^1].Step.Action == "click" && SameTarget(steps[^1].Step, step))
                steps.RemoveAt(steps.Count - 1);

            // Typing into the same field again replaces the earlier value.
            if (step.Action == "fill" && steps.Count > 0 &&
                steps[^1].Step.Action == "fill" && SameTarget(steps[^1].Step, step))
                steps.RemoveAt(steps.Count - 1);

            steps.Add((step, raw.Timestamp));
            if (step.Action != "navigate") lastActionAt = raw.Timestamp;
        }

        for (int i = 0; i < steps.Count; i++)
        {
            if (i > 0)
            {
                var gap = steps[i].At - steps[i - 1].At;
                if (gap >= MinRecordedPause && steps[i].Step.Action != "navigate")
                    script.Steps.Add(DelayStep(gap));
            }
            script.Steps.Add(steps[i].Step);
        }

        for (int i = 0; i < script.Steps.Count; i++)
            script.Steps[i].StepNumber = i + 1;

        return script;
    }

    private static ScriptStep? NavigateStep(RawEvent raw, ref string? lastUrl, DateTimeOffset? lastActionAt, int stepCount)
    {
        var url = raw.WindowTitle;
        if (string.IsNullOrWhiteSpace(url) || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return null;
        if (string.Equals(url, lastUrl, StringComparison.Ordinal)) return null;
        lastUrl = url;

        // Navigations caused by a recorded click or key press replay by themselves.
        if (stepCount > 0 && lastActionAt is { } at && raw.Timestamp - at < CausedNavigationWindow)
            return null;

        return new ScriptStep
        {
            Description = $"Open {url}",
            Action = "navigate",
            Parameters = new Dictionary<string, string?> { ["url"] = url }
        };
    }

    private static ScriptStep? ClickStep(RawEvent raw, BrowserTarget target)
    {
        var p = target.ToParameters();
        if (p.Count == 0) return null;
        return new ScriptStep
        {
            Description = $"Click {target.Describe()}",
            Action = "click",
            Parameters = p
        };
    }

    private static ScriptStep? InputStep(RawEvent raw, BrowserTarget target)
    {
        var p = target.ToParameters();
        if (p.Count == 0) return null;

        if (target.IsCheckable)
        {
            var on = string.Equals(raw.Value, "true", StringComparison.OrdinalIgnoreCase);
            return new ScriptStep
            {
                Description = $"{(on ? "Check" : "Uncheck")} {target.Describe()}",
                Action = on ? "check" : "uncheck",
                Parameters = p
            };
        }

        if (target.IsSelect)
        {
            p["value"] = raw.Value ?? string.Empty;
            return new ScriptStep
            {
                Description = $"Select \"{raw.Value}\" in {target.Describe()}",
                Action = "select",
                Parameters = p
            };
        }

        if (raw.IsPasswordField)
        {
            var secretName = SecretName(target);
            p["value"] = $"${{secret:{secretName}}}";
            return new ScriptStep
            {
                Description = $"Enter the password in {target.Describe()} (set TRACTO_SECRET_{secretName} on the worker)",
                Action = "fill",
                Parameters = p
            };
        }

        p["value"] = raw.Value ?? string.Empty;
        return new ScriptStep
        {
            Description = $"Enter \"{raw.Value}\" in {target.Describe()}",
            Action = "fill",
            Parameters = p
        };
    }

    private static ScriptStep? PressStep(RawEvent raw, BrowserTarget target)
    {
        if (string.IsNullOrWhiteSpace(raw.KeyValue)) return null;
        var p = target.ToParameters();
        p["key"] = raw.KeyValue;
        return new ScriptStep
        {
            Description = p.Count > 1 ? $"Press {raw.KeyValue} in {target.Describe()}" : $"Press {raw.KeyValue}",
            Action = "press",
            Parameters = p
        };
    }

    private static string SecretName(BrowserTarget t)
    {
        var basis = t.Label ?? t.Name ?? t.Placeholder ?? "password";
        var cleaned = Regex.Replace(basis, "[^A-Za-z0-9]+", "_").Trim('_').ToUpperInvariant();
        return string.IsNullOrEmpty(cleaned) ? "PASSWORD" : cleaned;
    }

    private static bool SameTarget(ScriptStep a, ScriptStep b)
    {
        foreach (var key in new[] { "role", "name", "label", "placeholder", "testId", "text", "css" })
        {
            a.Parameters.TryGetValue(key, out var va);
            b.Parameters.TryGetValue(key, out var vb);
            if (!string.Equals(va, vb, StringComparison.Ordinal)) return false;
        }
        return true;
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
}
