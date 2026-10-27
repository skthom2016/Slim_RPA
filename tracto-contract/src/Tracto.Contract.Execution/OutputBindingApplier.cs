#nullable enable

using System.Collections.Generic;
using System.Text.Json;

namespace Tracto.Contract.Execution;

// Applies the outputBinding of a completed step into the VariableSpace.
// C3 APPROVED (2026-10-23): Only runs when the step's terminal state is SUCCEEDED.
// FAILED, TIMED_OUT, and UNKNOWN steps do not produce output bindings.
public sealed class OutputBindingApplier
{
    private readonly IVariableSpace _vars;

    public OutputBindingApplier(IVariableSpace vars) => _vars = vars;

    // Applies all outputBinding entries for a completed step.
    // stepOutputs: the actual JSON outputs produced by the step action.
    // outputBinding: the binding map from the StepDefinition (varName -> outputField).
    // Returns any binding errors (type mismatch, undeclared var, missing output field).
    public IReadOnlyList<OutputBindingError> Apply(
        IReadOnlyDictionary<string, string> outputBinding,
        IReadOnlyDictionary<string, JsonElement> stepOutputs)
    {
        var errors = new List<OutputBindingError>();

        foreach (var (varName, outputField) in outputBinding)
        {
            if (!stepOutputs.TryGetValue(outputField, out var value))
            {
                errors.Add(new OutputBindingError(varName, outputField, "variable.outputFieldNotFound",
                    $"Step output field '{outputField}' not found in step outputs."));
                continue;
            }

            try { _vars.Write(varName, value); }
            catch (VariableSpaceException ex)
            {
                errors.Add(new OutputBindingError(varName, outputField, ex.ErrorDetail, ex.Message));
            }
        }

        return errors;
    }
}

public record OutputBindingError(string VarName, string OutputField, string ErrorDetail, string Message);
