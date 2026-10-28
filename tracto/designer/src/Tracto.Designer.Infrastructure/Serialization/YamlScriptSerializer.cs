namespace Tracto.Designer.Infrastructure.Serialization;

using System.Text;
using Tracto.Designer.Core.Scripting;

public sealed class YamlScriptSerializer : IScriptSerializer
{
    public string Serialize(Script script)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"id: {Escape(script.Id)}");
        sb.AppendLine($"name: {Escape(script.Name)}");
        sb.AppendLine($"version: {Escape(script.Version)}");
        sb.AppendLine($"targetRuntime: {Escape(script.TargetRuntime)}");
        sb.AppendLine("steps:");
        foreach (var step in script.Steps)
        {
            sb.AppendLine($"  - stepNumber: {step.StepNumber}");
            sb.AppendLine($"    description: {Escape(step.Description)}");
            sb.AppendLine($"    action: {Escape(step.Action)}");
            if (!string.IsNullOrEmpty(step.Condition))
                sb.AppendLine($"    condition: {Escape(step.Condition)}");
            if (step.Parameters is { Count: > 0 })
            {
                sb.AppendLine("    parameters:");
                foreach (var (k, v) in step.Parameters)
                    sb.AppendLine($"      {k}: {(v is null ? "null" : Escape(v))}");
            }
        }
        return sb.ToString();
    }

    public Script Deserialize(string content)
    {
        var s = new Script();
        var lines = content.Split('\n');
        ScriptStep? currentStep = null;
        bool inParams = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line)) continue;
            var trimmed = line.TrimStart();
            var indent = line.Length - trimmed.Length;

            if (indent == 0)
            {
                inParams = false;
                if (TryParsePair(trimmed, out var k0, out var v0))
                {
                    switch (k0)
                    {
                        case "id":            s.Id            = v0; break;
                        case "name":          s.Name          = v0; break;
                        case "version":       s.Version       = v0; break;
                        case "targetRuntime": s.TargetRuntime = v0; break;
                    }
                }
            }
            else if (indent == 2 && trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                currentStep = new ScriptStep();
                s.Steps.Add(currentStep);
                inParams = false;
                var rest = trimmed[2..];
                if (TryParsePair(rest, out var k2, out var v2) && k2 == "stepNumber" && int.TryParse(v2, out var n))
                    currentStep.StepNumber = n;
            }
            else if (indent == 4 && currentStep is not null)
            {
                if (trimmed == "parameters:") { inParams = true; continue; }
                inParams = false;
                if (TryParsePair(trimmed, out var k4, out var v4))
                {
                    switch (k4)
                    {
                        case "stepNumber":  if (int.TryParse(v4, out var n4)) currentStep.StepNumber = n4; break;
                        case "description": currentStep.Description = v4; break;
                        case "action":      currentStep.Action      = v4; break;
                        case "condition":   currentStep.Condition   = v4 == "null" ? null : v4; break;
                    }
                }
            }
            else if (indent == 6 && inParams && currentStep is not null)
            {
                if (TryParsePair(trimmed, out var pk, out var pv))
                    currentStep.Parameters[pk] = pv == "null" ? null : pv;
            }
        }
        return s;
    }

    private static string Escape(string? s)
    {
        if (s is null) return "null";
        if (s.Contains(':') || s.Contains('#') || s.Contains('"') ||
            s.StartsWith(' ') || s.EndsWith(' '))
            return $"\"{s.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
        return s;
    }

    private static bool TryParsePair(string line, out string key, out string value)
    {
        var idx = line.IndexOf(':');
        if (idx < 0) { key = value = ""; return false; }
        key = line[..idx].Trim();
        var raw = line[(idx + 1)..].Trim();
        value = raw.StartsWith('"') && raw.EndsWith('"') && raw.Length >= 2
            ? raw[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\")
            : raw;
        return true;
    }
}
