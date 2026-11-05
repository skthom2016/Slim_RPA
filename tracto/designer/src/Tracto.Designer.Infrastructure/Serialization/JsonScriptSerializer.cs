namespace Tracto.Designer.Infrastructure.Serialization;

using System.IO;
using System.Text.Json;
using Tracto.Designer.Core.Scripting;

public sealed class JsonScriptSerializer : IScriptSerializer
{
    private static readonly JsonSerializerOptions _write = new() { WriteIndented = true };

    public string Serialize(Script script) => JsonSerializer.Serialize(ScriptDocument.Build(script), _write);

    /// <summary>
    /// Reads a script file: a single-runtime script (targetRuntime + steps), a segmented script
    /// (segments), or either of them wrapped as {"schemaVersion":"1","script":{...}}.
    /// </summary>
    public Script Deserialize(string content)
    {
        using var document = JsonDocument.Parse(content);
        var doc = document.RootElement;
        if (doc.TryGetProperty("script", out var inner) && inner.ValueKind == JsonValueKind.Object)
            doc = inner;

        if (doc.TryGetProperty("scripts", out _))
            throw new InvalidDataException(
                "This file lists several scripts under \"scripts\". Import one script file at a time, " +
                "or combine the scripts into one file with \"segments\".");

        var s = new Script
        {
            Id      = doc.GetProperty("id").GetString() ?? "",
            Name    = doc.GetProperty("name").GetString() ?? "",
            Version = doc.GetProperty("version").GetString() ?? ""
        };

        if (doc.TryGetProperty("segments", out var segmentsEl) && segmentsEl.ValueKind == JsonValueKind.Array)
        {
            if (segmentsEl.GetArrayLength() == 0)
                throw new InvalidDataException("\"segments\" is empty. A segmented script needs at least one segment.");
            if (doc.TryGetProperty("params", out var paramsEl) && paramsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var kv in paramsEl.EnumerateObject())
                    s.Params[kv.Name] = kv.Value.ValueKind switch
                    {
                        JsonValueKind.String => kv.Value.GetString() ?? "",
                        JsonValueKind.Null   => "",
                        _                    => kv.Value.GetRawText()
                    };
            }
            foreach (var segEl in segmentsEl.EnumerateArray())
            {
                var seg = new ScriptSegment
                {
                    Name    = segEl.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    Runtime = (segEl.TryGetProperty("runtime", out var r) ? r.GetString() : null)?.ToLowerInvariant() ?? ""
                };
                if (segEl.TryGetProperty("mode", out var modeEl) && modeEl.ValueKind == JsonValueKind.String)
                    seg.Mode = modeEl.GetString()?.ToLowerInvariant() ?? "";
                if (segEl.TryGetProperty("profile", out var profileEl) && profileEl.ValueKind == JsonValueKind.String)
                    seg.Profile = profileEl.GetString() ?? "";
                if (segEl.TryGetProperty("steps", out var segSteps) && segSteps.ValueKind == JsonValueKind.Array)
                    foreach (var stepEl in segSteps.EnumerateArray())
                        seg.Steps.Add(ReadStep(stepEl));
                s.Segments.Add(seg);
            }
            return s;
        }

        s.TargetRuntime = doc.GetProperty("targetRuntime").GetString() ?? "";
        if (doc.TryGetProperty("steps", out var stepsEl))
        {
            foreach (var stepEl in stepsEl.EnumerateArray())
                s.Steps.Add(ReadStep(stepEl));
        }
        return s;
    }

    private static ScriptStep ReadStep(JsonElement stepEl)
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
        return step;
    }
}
