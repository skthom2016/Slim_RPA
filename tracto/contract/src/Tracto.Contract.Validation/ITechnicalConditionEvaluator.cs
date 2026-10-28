#nullable enable

using System.Text.Json;

namespace Tracto.Contract.Validation;

// Evaluates a JsonElement representing a TechnicalCondition.
// Returns true/false based on current runtime state provided via IEvaluationContext.
public interface ITechnicalConditionEvaluator
{
    // Validates the schema of a technical condition JSON element.
    // Evaluation is in Tracto.Contract.Execution, not here — validator only checks schema.
    ValidationResult ValidateConditionSchema(JsonElement condition);
}
