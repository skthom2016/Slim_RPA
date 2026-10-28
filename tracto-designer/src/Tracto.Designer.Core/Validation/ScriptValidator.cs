namespace Tracto.Designer.Core.Validation;

using System.Text.RegularExpressions;
using Tracto.Designer.Core.Scripting;
using static ValidationSeverity;

public sealed class ScriptValidator : IScriptValidator
{
    private static readonly Regex SemVer = new(@"^\d+\.\d+\.\d+$", RegexOptions.Compiled);

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
        if (string.IsNullOrWhiteSpace(script.TargetRuntime))
            errors.Add(new("VAL-004", Error, "TargetRuntime is required (desktop, browser, or terminal)."));

        // Empty script warning
        if (script.Steps.Count == 0)
            errors.Add(new("VAL-005", Warning, "Script has no steps. Add steps before publishing."));

        // Step-level checks
        for (int i = 0; i < script.Steps.Count; i++)
        {
            var step = script.Steps[i];
            int n = step.StepNumber;

            if (n != i + 1)
                errors.Add(new("VAL-010", Error, $"Step at index {i} has StepNumber={n}; expected {i + 1}.", n));
            if (string.IsNullOrWhiteSpace(step.Description))
                errors.Add(new("VAL-011", Error, $"Step {n} description is required.", n));
            if (string.IsNullOrWhiteSpace(step.Action))
                errors.Add(new("VAL-012", Error, $"Step {n} action is required.", n));
        }

        return new ScriptValidationResult(errors);
    }
}
