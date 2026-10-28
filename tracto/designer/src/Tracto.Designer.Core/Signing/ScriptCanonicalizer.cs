namespace Tracto.Designer.Core.Signing;

using System.Text;
using System.Text.Json;
using Tracto.Designer.Core.Scripting;

public static class ScriptCanonicalizer
{
    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = false };

    public static string Canonicalize(Script script)
    {
        var doc = new
        {
            id = script.Id,
            name = script.Name,
            version = script.Version,
            targetRuntime = script.TargetRuntime,
            steps = script.Steps
                .OrderBy(s => s.StepNumber)
                .Select(s => new
                {
                    stepNumber = s.StepNumber,
                    action = s.Action,
                    description = s.Description,
                    parameters = s.Parameters is { Count: > 0 }
                        ? s.Parameters.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value)
                        : null
                })
        };
        return JsonSerializer.Serialize(doc, _opts);
    }

    public static byte[] CanonicalBytes(Script script) =>
        Encoding.UTF8.GetBytes(Canonicalize(script));
}
