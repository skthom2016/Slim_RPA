namespace Tracto.Designer.Core.Validation;

public sealed record ValidationError(
    string RuleId,
    ValidationSeverity Severity,
    string Message,
    int? StepNumber = null);
