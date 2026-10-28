#nullable enable

using System.Text.Json;
using Tracto.Contract.Models;
using Tracto.Contract.Models.Envelope;

namespace Tracto.Contract.Testing;

/// <summary>
/// Fluent builder for <see cref="StepDefinition"/> with per-field overrides.
/// </summary>
public sealed class StepDefinitionBuilder
{
    private readonly string _stepId;
    private readonly string _action;
    private int? _timeoutMs;
    private RetryClass? _retryClass;
    private readonly Dictionary<string, string> _outputBinding = new();
    private readonly Dictionary<string, JsonElement> _inputs = new();

    /// <param name="stepId">The unique step identifier within the envelope.</param>
    /// <param name="action">The action token (e.g. "click", "navigate", "service.invoke").</param>
    public StepDefinitionBuilder(string stepId, string action)
    {
        _stepId = stepId;
        _action = action;
    }

    /// <summary>Set a per-step timeout in milliseconds.</summary>
    public StepDefinitionBuilder WithTimeout(int ms)
    {
        _timeoutMs = ms;
        return this;
    }

    /// <summary>Set the retry class for this step.</summary>
    public StepDefinitionBuilder WithRetryClass(RetryClass rc)
    {
        _retryClass = rc;
        return this;
    }

    /// <summary>
    /// Add an output binding that maps a step output field to a script variable.
    /// </summary>
    /// <param name="varName">The script variable that receives the value.</param>
    /// <param name="stepOutputField">The field name produced by the step's output.</param>
    public StepDefinitionBuilder WithOutputBinding(string varName, string stepOutputField)
    {
        _outputBinding[varName] = stepOutputField;
        return this;
    }

    /// <summary>Add a raw JSON input element.</summary>
    public StepDefinitionBuilder WithInput(string name, JsonElement value)
    {
        _inputs[name] = value;
        return this;
    }

    /// <summary>Build and return the configured <see cref="StepDefinition"/>.</summary>
    public StepDefinition Build() => new(
        StepId: _stepId,
        Action: _action,
        Inputs: _inputs.Count > 0 ? _inputs : null,
        OutputBinding: _outputBinding.Count > 0 ? _outputBinding : null,
        TimeoutMs: _timeoutMs,
        RetryClass: _retryClass);
}
