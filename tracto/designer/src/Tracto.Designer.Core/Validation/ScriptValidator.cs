namespace Tracto.Designer.Core.Validation;

using System.Text.RegularExpressions;
using Tracto.Designer.Core.Scripting;
using static ValidationSeverity;

public sealed class ScriptValidator : IScriptValidator
{
    private static readonly Regex SemVer = new(@"^\d+\.\d+\.\d+$", RegexOptions.Compiled);

    // A profile name becomes a folder name on the worker, so it is kept to safe characters.
    private static readonly Regex ProfileNamePattern =
        new(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.Compiled);

    private static Regex ProfileName() => ProfileNamePattern;

    public ScriptValidationResult Validate(Script script)
    {
        var errors = new List<ValidationError>();

        // Script-level checks
        if (string.IsNullOrWhiteSpace(script.Id))
            errors.Add(new("VAL-001", Error, "Script Id is required."));
        if (string.IsNullOrWhiteSpace(script.Name))
            errors.Add(new("VAL-002", Error, "Script Name is required."));
        if (string.IsNullOrWhiteSpace(script.Version) || !SemVer.IsMatch(script.Version))
            errors.Add(new("VAL-003", Error, "Script Version must follow semver format (e.g. 1.0.0)."));

        if (script.IsSegmented)
            ValidateSegmented(script, errors);
        else
            ValidateFlat(script, errors);

        ValidateDataFlow(script, errors);

        return new ScriptValidationResult(errors);
    }

    private static void ValidateFlat(Script script, List<ValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(script.TargetRuntime))
            errors.Add(new("VAL-004", Error, "TargetRuntime is required (desktop, browser, or terminal)."));

        // Empty script warning
        if (script.Steps.Count == 0)
            errors.Add(new("VAL-005", Warning, "Script has no steps. Add steps before publishing."));

        ValidateSteps(script.Steps, "", errors);
    }

    private static void ValidateSegmented(Script script, List<ValidationError> errors)
    {
        if (script.Steps.Count > 0)
            errors.Add(new("VAL-024", Error,
                "A segmented script keeps all steps inside its segments; remove the top-level steps."));

        foreach (var key in script.Params.Keys)
        {
            if (string.IsNullOrWhiteSpace(key))
                errors.Add(new("VAL-025", Error, "A parameter name is empty."));
        }

        for (int i = 0; i < script.Segments.Count; i++)
        {
            var seg = script.Segments[i];
            var label = $"Segment {i + 1} ({(string.IsNullOrWhiteSpace(seg.Name) ? "unnamed" : seg.Name)})";

            if (!ScriptSegment.Runtimes.Contains(seg.Runtime, StringComparer.OrdinalIgnoreCase))
                errors.Add(new("VAL-020", Error, $"{label}: runtime must be desktop, browser or terminal."));
            if (seg.Steps.Count == 0)
                errors.Add(new("VAL-021", Error, $"{label} has no steps."));

            if (!string.IsNullOrWhiteSpace(seg.Mode))
            {
                if (!seg.IsBrowser)
                    errors.Add(new("VAL-022", Error, $"{label}: mode applies to a browser segment only."));
                else if (!seg.Mode.Equals("headed", StringComparison.OrdinalIgnoreCase) &&
                         !seg.Mode.Equals("headless", StringComparison.OrdinalIgnoreCase))
                    errors.Add(new("VAL-022", Error, $"{label}: mode must be headed or headless."));
            }

            if (!string.IsNullOrWhiteSpace(seg.Profile))
            {
                if (!seg.IsBrowser)
                    errors.Add(new("VAL-026", Error, $"{label}: profile applies to a browser segment only."));
                else if (!ProfileName().IsMatch(seg.Profile.Trim()))
                    errors.Add(new("VAL-026", Error,
                        $"{label}: profile must be 1 to 64 characters of letters, digits, dash, underscore or dot."));
            }

            ValidateSteps(seg.Steps, label + ": ", errors);
        }
    }

    /// <summary>
    /// A reference nothing produces is left in the text by the runtimes rather than failing, so
    /// an unnoticed typo reaches the live run as the literal text of the reference.
    /// </summary>
    private static void ValidateDataFlow(Script script, List<ValidationError> errors)
    {
        if (script.IsSegmented)
        {
            for (int i = 0; i < script.Segments.Count; i++)
                CheckReferences(script, script.Segments[i], script.Segments[i].Steps,
                    $"Segment {i + 1} ({script.Segments[i].Name}): ", errors);
        }
        else
        {
            CheckReferences(script, null, script.Steps, "", errors);
        }
    }

    private static void CheckReferences(Script script, ScriptSegment? segment,
        IList<ScriptStep> steps, string prefix, List<ValidationError> errors)
    {
        foreach (var step in steps)
        {
            foreach (var missing in DataFlow.UnresolvedIn(script, segment, step))
                errors.Add(new("VAL-030", Error,
                    $"{prefix}Step {step.StepNumber} reads ${{{missing}}}, which nothing before it produces.",
                    step.StepNumber));
        }
    }

    private static void ValidateSteps(IList<ScriptStep> steps, string prefix, List<ValidationError> errors)
    {
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            int n = step.StepNumber;

            if (n != i + 1)
                errors.Add(new("VAL-010", Error, $"{prefix}Step at index {i} has StepNumber={n}; expected {i + 1}.", n));
            if (string.IsNullOrWhiteSpace(step.Description))
                errors.Add(new("VAL-011", Error, $"{prefix}Step {n} description is required.", n));
            if (string.IsNullOrWhiteSpace(step.Action))
                errors.Add(new("VAL-012", Error, $"{prefix}Step {n} action is required.", n));
        }
    }
}
