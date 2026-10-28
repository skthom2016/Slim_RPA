namespace Tracto.Designer.Infrastructure.Serialization;

using System.Text.Json;
using Tracto.Designer.Core.Scripting;

public sealed class JsonScriptSerializer : IScriptSerializer
{
    private static readonly JsonSerializerOptions _write = new() { WriteIndented = true };

    public string Serialize(Script script)
    {
        var doc = new
        {
            id            = script.Id,
            name          = script.Name,
            version       = script.Version,
            targetRuntime = script.TargetRuntime,
            steps         = script.Steps.Select(s => new
            {
                stepNumber  = s.StepNumber,
                description = s.Description,
                action      = s.Action,
                condition   = s.Condition,
                parameters  = s.Parameters
            })
        };
        return JsonSerializer.Serialize(doc, _write);
    }

    public Script Deserialize(string content)
    {
        using var document = JsonDocument.Parse(content);
        var doc = document.RootElement;
        var s = new Script
        {
            Id            = doc.GetProperty("id").GetString() ?? "",
            Name          = doc.GetProperty("name").GetString() ?? "",
            Version       = doc.GetProperty("version").GetString() ?? "",
            TargetRuntime = doc.GetProperty("targetRuntime").GetString() ?? ""
        };
        if (doc.TryGetProperty("steps", out var stepsEl))
        {
            foreach (var stepEl in stepsEl.EnumerateArray())
            {
                var step = new ScriptStep
                {
                    StepNumber  = stepEl.GetProperty("stepNumber").GetInt32(),
                    Description = stepEl.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                    Action      = stepEl.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "",
                    Condition   = stepEl.TryGetProperty("condition", out var c) && c.ValueKind != JsonValueKind.Null
                                    ? c.GetString() : null
                };
                if (stepEl.TryGetProperty("parameters", out var pEl) && pEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var kv in pEl.EnumerateObject())
                        step.Parameters[kv.Name] = kv.Value.ValueKind == JsonValueKind.Null
                            ? null : kv.Value.GetString();
                }
                s.Steps.Add(step);
            }
        }
        return s;
    }
}
