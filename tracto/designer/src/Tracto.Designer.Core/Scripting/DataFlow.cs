namespace Tracto.Designer.Core.Scripting;

using System.Text.RegularExpressions;

/// <summary>Where a piece of data an author can use in a step comes from.</summary>
public enum DataOrigin
{
    /// <summary>A default declared on the script, overridable when a job is submitted.</summary>
    ScriptParameter,
    /// <summary>A step earlier in the same segment, read without the param prefix.</summary>
    EarlierStep,
    /// <summary>A step in a segment that runs before this one, read as a parameter.</summary>
    EarlierSegment
}

/// <summary>One value a step can reference, and where it comes from.</summary>
public sealed record DataReference(string Reference, string Name, DataOrigin Origin, string ProducedBy)
{
    public string OriginLabel => Origin switch
    {
        DataOrigin.ScriptParameter => "Script parameter",
        DataOrigin.EarlierStep     => "Earlier step",
        _                          => "Earlier segment"
    };
}

/// <summary>
/// Works out what data a step can read, and what it reads that nothing produces.
///
/// A step that carries a <c>storeAs</c> exposes its action's outputs. Inside the same segment a
/// later step reads them as <c>${name.field}</c>. Across segments the Orchestrator copies a
/// finished segment's outputs into the parameters of the ones after it, so there they are read
/// as <c>${param.name.field}</c>. That copy is the only way data crosses a segment boundary.
/// </summary>
public static class DataFlow
{
    private static readonly Regex Token = new(@"\$\{([^}]+)\}", RegexOptions.Compiled);

    /// <summary>Names the runtimes resolve themselves, so a step may always reference them.</summary>
    private static readonly string[] Builtins = ["now.iso", "now.local", "now.date", "now.time"];

    /// <summary>
    /// Everything <paramref name="step"/> may reference, newest last. Pass the step to see the
    /// earlier steps of its own segment; pass null to see only what the segment starts with.
    /// </summary>
    public static IReadOnlyList<DataReference> AvailableTo(Script script, ScriptSegment? segment, ScriptStep? step = null)
    {
        var found = new List<DataReference>();

        foreach (var name in script.Params.Keys.OrderBy(k => k, StringComparer.Ordinal))
            found.Add(new DataReference($"${{param.{name}}}", $"param.{name}",
                DataOrigin.ScriptParameter, "Script parameters"));

        if (script.IsSegmented)
        {
            var upTo = segment is null ? script.Segments.Count : script.Segments.IndexOf(segment);
            if (upTo < 0) upTo = script.Segments.Count;
            for (int i = 0; i < upTo; i++)
            {
                var earlier = script.Segments[i];
                foreach (var producer in earlier.Steps)
                    Add(found, producer, earlier.Runtime, DataOrigin.EarlierSegment,
                        $"Segment {i + 1} ({earlier.Name}), step {producer.StepNumber}", prefix: "param.");
            }
        }

        // Steps before this one in the same segment, which need no param prefix.
        var ownSteps = segment?.Steps ?? (script.IsSegmented ? null : script.Steps);
        if (ownSteps is not null && step is not null)
        {
            var runtime = segment?.Runtime ?? script.TargetRuntime;
            foreach (var producer in ownSteps)
            {
                if (ReferenceEquals(producer, step)) break;
                Add(found, producer, runtime, DataOrigin.EarlierStep, $"Step {producer.StepNumber}", prefix: "");
            }
        }

        return found;
    }

    private static void Add(List<DataReference> into, ScriptStep producer, string runtime,
        DataOrigin origin, string producedBy, string prefix)
    {
        var storeAs = StoreAsOf(producer);
        if (storeAs is null) return;

        into.Add(new DataReference($"${{{prefix}{storeAs}}}", prefix + storeAs, origin, producedBy));
        foreach (var field in ActionCatalog.OutputsOf(runtime, producer.Action))
            into.Add(new DataReference($"${{{prefix}{storeAs}.{field}}}", $"{prefix}{storeAs}.{field}", origin, producedBy));
    }

    /// <summary>The name a step publishes its outputs under, or null when it publishes none.</summary>
    public static string? StoreAsOf(ScriptStep step) =>
        step.Parameters.TryGetValue(ActionCatalog.StoreAs, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v!.Trim()
            : null;

    /// <summary>Every <c>${...}</c> name a step's parameter values mention, without duplicates.</summary>
    public static IReadOnlyList<string> ReferencesIn(ScriptStep step)
    {
        var names = new List<string>();
        foreach (var value in step.Parameters.Values)
        {
            if (value is null) continue;
            foreach (Match m in Token.Matches(value))
            {
                var name = m.Groups[1].Value.Trim();
                if (name.Length > 0 && !names.Contains(name, StringComparer.Ordinal)) names.Add(name);
            }
        }
        return names;
    }

    /// <summary>
    /// References the step makes that nothing before it produces. The runtimes leave an
    /// unresolved reference in the text rather than failing, so a typo reaches the live run as
    /// literal "${param.whatever}" unless it is caught here.
    /// </summary>
    public static IReadOnlyList<string> UnresolvedIn(Script script, ScriptSegment? segment, ScriptStep step)
    {
        var available = AvailableTo(script, segment, step).Select(d => d.Name).ToList();
        var unresolved = new List<string>();

        foreach (var name in ReferencesIn(step))
        {
            if (name.StartsWith("secret:", StringComparison.Ordinal)) continue;
            if (Builtins.Contains(name, StringComparer.Ordinal)) continue;
            // An action outside the catalogue has outputs we cannot know, so a reference below
            // a name that is produced counts as resolved.
            if (available.Any(a => a == name || name.StartsWith(a + ".", StringComparison.Ordinal))) continue;
            unresolved.Add(name);
        }
        return unresolved;
    }
}
