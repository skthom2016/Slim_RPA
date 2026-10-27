#nullable enable

using System.Text.Json;

namespace Tracto.Contract.Validation;

// Validates TechnicalCondition JSON against the allowlist grammar.
// Evaluation (true/false based on runtime state) is in Tracto.Contract.Execution.
public sealed class TechnicalConditionValidator : ITechnicalConditionEvaluator
{
    // Allowed predicate types (closed allowlist — no business expressions)
    private static readonly HashSet<string> AllowedPredicateTypes = new(StringComparer.Ordinal)
    {
        "state.equals",
        "element.visible", "element.enabled", "element.exists", "element.textEquals",
        "api.outcomeCategoryEquals", "api.actionCodeEquals",
        "command.exitCodeEquals", "command.outputContains",
        "variable.isSet"
    };

    // Implements ITechnicalConditionEvaluator.ValidateConditionSchema
    public ValidationResult ValidateConditionSchema(JsonElement condition) => Validate(condition, 0);

    public ValidationResult Validate(JsonElement condition, int depth = 0)
    {
        if (depth > 10)
            return ValidationResult.Fail(new ValidationDiagnostic(ValidationSeverity.Error,
                "CONDITION_DEPTH_EXCEEDED", "Technical condition nesting exceeds maximum depth of 10."));

        if (condition.ValueKind != JsonValueKind.Object)
            return ValidationResult.Fail(new ValidationDiagnostic(ValidationSeverity.Error,
                "INVALID_CONDITION", "Condition must be a JSON object."));

        // Check for composite: all / any / not
        if (condition.TryGetProperty("all", out var all))
            return ValidateArray(all, depth);
        if (condition.TryGetProperty("any", out var any))
            return ValidateArray(any, depth);
        if (condition.TryGetProperty("not", out var not))
            return Validate(not, depth + 1);

        // Must be a predicate — check "type" field
        if (!condition.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
            return ValidationResult.Fail(new ValidationDiagnostic(ValidationSeverity.Error,
                "MISSING_PREDICATE_TYPE", "Predicate condition must have a 'type' field."));

        var predicateType = typeEl.GetString()!;
        if (!AllowedPredicateTypes.Contains(predicateType))
            return ValidationResult.Fail(new ValidationDiagnostic(ValidationSeverity.Error,
                "DISALLOWED_PREDICATE_TYPE",
                $"Predicate type '{predicateType}' is not in the allowed list. " +
                $"Allowed: [{string.Join(", ", AllowedPredicateTypes)}]. " +
                "Domain expressions, arithmetic, and business logic are not permitted in technical conditions.",
                "type", predicateType));

        return ValidationResult.Ok();
    }

    private ValidationResult ValidateArray(JsonElement array, int depth)
    {
        if (array.ValueKind != JsonValueKind.Array)
            return ValidationResult.Fail(new ValidationDiagnostic(ValidationSeverity.Error,
                "INVALID_CONDITION_ARRAY", "Composite condition 'all'/'any' must be an array."));
        if (array.GetArrayLength() == 0)
            return ValidationResult.Fail(new ValidationDiagnostic(ValidationSeverity.Error,
                "EMPTY_CONDITION_ARRAY", "Composite condition array must have at least one item."));
        var diagnostics = new List<ValidationDiagnostic>();
        foreach (var item in array.EnumerateArray())
        {
            var result = Validate(item, depth + 1);
            if (!result.IsValid) diagnostics.AddRange(result.Diagnostics);
        }
        return diagnostics.Count == 0 ? ValidationResult.Ok() : ValidationResult.Fail(diagnostics);
    }
}
