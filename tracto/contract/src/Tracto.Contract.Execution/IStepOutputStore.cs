#nullable enable

using System.Collections.Generic;
using System.Text.Json;
using Tracto.Contract.Models;

namespace Tracto.Contract.Execution;

public interface IStepOutputStore
{
    void RecordOutputs(string stepId, IReadOnlyDictionary<string, JsonElement> outputs, StepState finalState);
    JsonElement? GetOutput(string stepId, string field);
    StepState? GetStepState(string stepId);
    IReadOnlyList<string> CompletedStepIds { get; }
}
