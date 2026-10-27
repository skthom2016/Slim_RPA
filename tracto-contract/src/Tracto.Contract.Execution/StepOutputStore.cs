#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Tracto.Contract.Models;

namespace Tracto.Contract.Execution;

public sealed class StepOutputStore : IStepOutputStore
{
    private readonly Dictionary<string, IReadOnlyDictionary<string, JsonElement>> _outputs
        = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StepState> _states
        = new(StringComparer.Ordinal);

    public void RecordOutputs(string stepId, IReadOnlyDictionary<string, JsonElement> outputs, StepState finalState)
    {
        _outputs[stepId] = outputs;
        _states[stepId] = finalState;
    }

    public JsonElement? GetOutput(string stepId, string field)
    {
        if (_outputs.TryGetValue(stepId, out var outs) && outs.TryGetValue(field, out var v))
            return v;
        return null;
    }

    public StepState? GetStepState(string stepId)
        => _states.TryGetValue(stepId, out var s) ? s : null;

    public IReadOnlyList<string> CompletedStepIds => _states.Keys.ToList();
}
