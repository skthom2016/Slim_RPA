namespace Tracto.Designer.Core.Scripting;

/// <summary>One thing wrong with a step, and whether it will stop the run or merely waste it.</summary>
public sealed record StepIssue(string RuleId, bool IsError, string Message);

/// <summary>
/// Checks a step against what its action actually takes. Every one of these otherwise surfaces
/// only when the step runs: a missing parameter fails the step, an unknown one is ignored in
/// silence, and a step that names no element cannot resolve anything at all.
/// </summary>
public static class StepRules
{
    public static IReadOnlyList<StepIssue> Check(string runtime, ScriptStep step)
    {
        var issues = new List<StepIssue>();
        var definition = ActionCatalog.Find(runtime, step.Action);

        // An action outside the catalogue may still be valid, so nothing below can be judged.
        if (definition is null)
        {
            if (!string.IsNullOrWhiteSpace(step.Action))
                issues.Add(new("VAL-031", false,
                    $"action '{step.Action}' is not one the {Name(runtime)} runtime is known to have."));
            return issues;
        }

        CheckRequired(definition, step, issues);
        CheckUnknown(definition, step, issues);
        CheckElement(definition, step, issues);
        CheckStoreAs(definition, step, issues);
        CheckNumbers(runtime, step, issues);
        return issues;
    }

    private static void CheckRequired(ActionDefinition definition, ScriptStep step, List<StepIssue> issues)
    {
        foreach (var name in definition.Required)
        {
            if (!step.Parameters.TryGetValue(name, out var value))
                issues.Add(new("VAL-032", true, $"'{definition.Name}' needs a '{name}' parameter."));
            else if (string.IsNullOrWhiteSpace(value))
                issues.Add(new("VAL-033", true, $"'{name}' is empty, and '{definition.Name}' needs a value for it."));
        }
    }

    private static void CheckUnknown(ActionDefinition definition, ScriptStep step, List<StepIssue> issues)
    {
        foreach (var name in step.Parameters.Keys)
        {
            if (name == ActionCatalog.StoreAs) continue;
            if (definition.AllParameters.Contains(name, StringComparer.Ordinal)) continue;
            issues.Add(new("VAL-034", false,
                $"'{definition.Name}' does not use a '{name}' parameter, so it is ignored."));
        }
    }

    private static void CheckElement(ActionDefinition definition, ScriptStep step, List<StepIssue> issues)
    {
        if (!definition.NeedsElement) return;

        var named = ActionCatalog.ElementSelectors
            .Where(s => step.Parameters.TryGetValue(s, out var v) && !string.IsNullOrWhiteSpace(v))
            .ToList();

        if (named.Count == 0)
        {
            issues.Add(new("VAL-035", true,
                $"'{definition.Name}' works on an element but names none. Add one of: "
                + string.Join(", ", ActionCatalog.ElementSelectors) + "."));
        }
        else if (named.Count > 1)
        {
            issues.Add(new("VAL-036", false,
                $"the element is named {named.Count} ways ({string.Join(", ", named)}); "
                + $"only '{named[0]}' is used."));
        }

        // A name on its own qualifies nothing.
        if (step.Parameters.TryGetValue("name", out var roleName) && !string.IsNullOrWhiteSpace(roleName)
            && !(step.Parameters.TryGetValue("role", out var role) && !string.IsNullOrWhiteSpace(role)))
        {
            issues.Add(new("VAL-037", false,
                "'name' describes a 'role' and does nothing on its own. Add a 'role', or name the element another way."));
        }
    }

    private static void CheckStoreAs(ActionDefinition definition, ScriptStep step, List<StepIssue> issues)
    {
        if (DataFlow.StoreAsOf(step) is null) return;
        if (definition.ProducesData) return;
        issues.Add(new("VAL-038", false,
            $"'{definition.Name}' produces no data, so naming its result publishes nothing."));
    }

    private static void CheckNumbers(string runtime, ScriptStep step, List<StepIssue> issues)
    {
        foreach (var name in ActionCatalog.NumericParameters)
        {
            if (!step.Parameters.TryGetValue(name, out var raw) || string.IsNullOrWhiteSpace(raw)) continue;
            if (raw.Contains("${", StringComparison.Ordinal)) continue;   // filled in at run time

            if (!int.TryParse(raw.Trim(), out var ms) || ms < 0)
            {
                issues.Add(new("VAL-039", true, $"'{name}' must be a whole number of milliseconds, not '{raw}'."));
                continue;
            }

            if (name == "ms" && IsDesktop(runtime) && ms > ActionCatalog.MaxDesktopDelayMs)
                issues.Add(new("VAL-040", true,
                    $"a desktop delay cannot be longer than {ActionCatalog.MaxDesktopDelayMs} ms."));
        }
    }

    private static bool IsDesktop(string runtime) =>
        string.IsNullOrWhiteSpace(runtime) || runtime.Equals("desktop", StringComparison.OrdinalIgnoreCase);

    private static string Name(string runtime) => IsDesktop(runtime) ? "desktop" : runtime.ToLowerInvariant();
}
