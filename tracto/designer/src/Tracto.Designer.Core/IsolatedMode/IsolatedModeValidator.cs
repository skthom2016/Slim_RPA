namespace Tracto.Designer.Core.IsolatedMode;

using Tracto.Designer.Core.Scripting;

public sealed class IsolatedModeValidator
{
    public IsolatedValidationResult Validate(Script script, IsolatedModeSettings settings)
    {
        if (!settings.IsEnabled) return IsolatedValidationResult.Compliant();

        var violations = new List<IsolatedViolation>();

        // Rule 1: Browser runtime not allowed in isolated mode
        if (script.TargetRuntime.Equals("browser", StringComparison.OrdinalIgnoreCase))
            violations.Add(new("ISO-001", "Browser runtime requires network access — not allowed in isolated mode."));

        if (!settings.RestrictNetworkAccess) return new IsolatedValidationResult(violations);

        // Rules only checked when RestrictNetworkAccess=true
        foreach (var step in script.Steps)
        {
            foreach (var (key, value) in step.Parameters)
            {
                if (value is null) continue;

                if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    violations.Add(new("ISO-002",
                        $"Step {step.StepNumber} parameter '{key}' contains a network URL. Remove or replace with a local path.",
                        step.StepNumber));

                if (value.StartsWith("\\\\", StringComparison.Ordinal) ||
                    value.StartsWith("//", StringComparison.Ordinal))
                    violations.Add(new("ISO-003",
                        $"Step {step.StepNumber} parameter '{key}' contains a UNC network path.",
                        step.StepNumber));
            }
        }

        return new IsolatedValidationResult(violations);
    }
}
