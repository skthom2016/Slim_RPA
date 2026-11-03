namespace Tracto.Designer.Core.Signing;

using System.Text;
using System.Text.Json;
using Tracto.Designer.Core.Scripting;

public static class ScriptCanonicalizer
{
    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = false };

    public static string Canonicalize(Script script)
    {
        if (script.IsSegmented)
        {
            var segmented = new
            {
                id = script.Id,
                name = script.Name,
                version = script.Version,
                @params = script.Params.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                                       .ToDictionary(kv => kv.Key, kv => kv.Value),
                segments = script.Segments.Select(g => new
                {
                    name = g.Name,
                    runtime = g.Runtime,
                    mode = g.Mode,
                    steps = CanonicalSteps(g.Steps)
                })
            };
            return JsonSerializer.Serialize(segmented, _opts);
        }

        var doc = new
        {
            id = script.Id,
            name = script.Name,
            version = script.Version,
            targetRuntime = script.TargetRuntime,
            steps = CanonicalSteps(script.Steps)
        };
        return JsonSerializer.Serialize(doc, _opts);
    }

    private static IEnumerable<object> CanonicalSteps(IEnumerable<ScriptStep> steps) =>
        steps.OrderBy(s => s.StepNumber)
             .Select(s => (object)new
             {
                 stepNumber = s.StepNumber,
                 action = s.Action,
                 description = s.Description,
                 parameters = s.Parameters is { Count: > 0 }
                     ? s.Parameters.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value)
                     : null
             });

    public static byte[] CanonicalBytes(Script script) =>
        Encoding.UTF8.GetBytes(Canonicalize(script));
}
