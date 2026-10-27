#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tracto.Contract.Models;

namespace Tracto.Contract.Execution;

// Resolves ref-paths from step inputs at execution time.
// Supported ref formats:
//   "inputs.<name>"                    → from the run's input dictionary
//   "vars.<name>"                      → from the VariableSpace (throws VariableSpaceException on uninit/undeclared)
//   "steps.<stepId>.outputs.<field>"   → from the StepOutputStore
//   "steps.<stepId>.result.state"      → from completed step state
// Any other ref format emits VALIDATION errorCategory with detail "input.unknownRefPath".
public sealed class InputResolver
{
    private readonly IReadOnlyDictionary<string, JsonElement> _runInputs;
    private readonly IVariableSpace _vars;
    private readonly IStepOutputStore _outputStore;

    public InputResolver(
        IReadOnlyDictionary<string, JsonElement> runInputs,
        IVariableSpace vars,
        IStepOutputStore outputStore)
    {
        _runInputs = runInputs;
        _vars = vars;
        _outputStore = outputStore;
    }

    // Resolves a single ref path. Returns (value, error). If error is non-null, resolution failed.
    public (JsonElement? Value, InputResolutionError? Error) Resolve(string refPath)
    {
        if (refPath.StartsWith("inputs.", StringComparison.Ordinal))
        {
            var name = refPath["inputs.".Length..];
            if (_runInputs.TryGetValue(name, out var v)) return (v, null);
            return (null, new InputResolutionError("input.notFound", $"Run input '{name}' not found."));
        }

        if (refPath.StartsWith("vars.", StringComparison.Ordinal))
        {
            var name = refPath["vars.".Length..];
            try { return (_vars.Read(name), null); }
            catch (VariableSpaceException ex)
            { return (null, new InputResolutionError(ex.ErrorDetail, ex.Message)); }
        }

        if (refPath.StartsWith("steps.", StringComparison.Ordinal))
        {
            // "steps.<stepId>.outputs.<field>" or "steps.<stepId>.result.state"
            var parts = refPath.Split('.', 4);
            if (parts.Length < 4)
                return (null, new InputResolutionError("input.malformedRef", $"Ref path '{refPath}' is malformed."));

            var stepId = parts[1];
            var segment = parts[2];
            var field = parts[3];

            if (segment == "outputs")
            {
                var val = _outputStore.GetOutput(stepId, field);
                if (val is null)
                    return (null, new InputResolutionError("input.stepOutputNotFound",
                        $"Step '{stepId}' output field '{field}' not found."));
                return (val, null);
            }

            if (segment == "result" && field == "state")
            {
                var state = _outputStore.GetStepState(stepId);
                if (state is null)
                    return (null, new InputResolutionError("input.stepNotComplete",
                        $"Step '{stepId}' has not completed."));
                var json = JsonSerializer.SerializeToElement(state.Value.ToString());
                return (json, null);
            }

            return (null, new InputResolutionError("input.unknownRefSegment",
                $"Unknown ref segment '{segment}' in '{refPath}'."));
        }

        return (null, new InputResolutionError("input.unknownRefPath",
            $"Ref path '{refPath}' does not match any known pattern (inputs.*, vars.*, steps.*)."));
    }

    // Resolves all inputs for a step, returning a dictionary of resolved values and any errors.
    public InputResolutionResult ResolveAll(IReadOnlyDictionary<string, JsonElement> rawInputs)
    {
        var resolved = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var errors = new List<InputResolutionError>();

        foreach (var (key, value) in rawInputs)
        {
            // If value is an object with a "ref" property, resolve it
            if (value.ValueKind == JsonValueKind.Object
                && value.TryGetProperty("ref", out var refEl)
                && refEl.ValueKind == JsonValueKind.String)
            {
                var (resolved2, error) = Resolve(refEl.GetString()!);
                if (error is not null)
                    errors.Add(error with { InputKey = key });
                else
                    resolved[key] = resolved2!.Value;
            }
            else
            {
                // Literal value — no resolution needed
                resolved[key] = value;
            }
        }

        return new InputResolutionResult(resolved, errors);
    }
}

public record InputResolutionError(string Detail, string Message, string? InputKey = null);

public record InputResolutionResult(
    IReadOnlyDictionary<string, JsonElement> Resolved,
    IReadOnlyList<InputResolutionError> Errors)
{
    public bool HasErrors => Errors.Count > 0;
}
