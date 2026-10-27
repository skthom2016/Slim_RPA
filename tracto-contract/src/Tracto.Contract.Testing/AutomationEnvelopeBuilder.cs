#nullable enable

using Tracto.Contract.Models;
using Tracto.Contract.Models.Envelope;

namespace Tracto.Contract.Testing;

/// <summary>
/// Fluent builder for <see cref="AutomationEnvelope"/> with sensible defaults
/// suitable for unit and conformance tests.
/// </summary>
public sealed class AutomationEnvelopeBuilder
{
    private string _schemaVersion = "1.0";
    private string _automationId = "test/automation/default";
    private RuntimeType _runtimeType = RuntimeType.Desktop;
    private readonly List<string> _policyRefs = new();
    private readonly Dictionary<string, InputDeclaration> _inputs = new();
    private readonly Dictionary<string, OutputDeclaration> _outputs = new();
    private readonly Dictionary<string, VarDeclaration> _vars = new();
    private readonly List<StepDefinition> _steps = new();

    /// <summary>Override the schema version (default: "1.0").</summary>
    public AutomationEnvelopeBuilder WithSchemaVersion(string schemaVersion)
    {
        _schemaVersion = schemaVersion;
        return this;
    }

    /// <summary>Override the automation identifier (default: "test/automation/default").</summary>
    public AutomationEnvelopeBuilder WithAutomationId(string automationId)
    {
        _automationId = automationId;
        return this;
    }

    /// <summary>Override the runtime type (default: <see cref="RuntimeType.Desktop"/>).</summary>
    public AutomationEnvelopeBuilder WithRuntimeType(RuntimeType runtimeType)
    {
        _runtimeType = runtimeType;
        return this;
    }

    /// <summary>Append a policy reference.</summary>
    public AutomationEnvelopeBuilder WithPolicyRef(string policyRef)
    {
        _policyRefs.Add(policyRef);
        return this;
    }

    /// <summary>Declare an input parameter.</summary>
    public AutomationEnvelopeBuilder WithInput(string name, InputDeclaration declaration)
    {
        _inputs[name] = declaration;
        return this;
    }

    /// <summary>Declare an output parameter.</summary>
    public AutomationEnvelopeBuilder WithOutput(string name, OutputDeclaration declaration)
    {
        _outputs[name] = declaration;
        return this;
    }

    /// <summary>Declare a script-scoped variable.</summary>
    public AutomationEnvelopeBuilder WithVar(string name, VarDeclaration declaration)
    {
        _vars[name] = declaration;
        return this;
    }

    /// <summary>Append a step definition.</summary>
    public AutomationEnvelopeBuilder WithStep(StepDefinition step)
    {
        _steps.Add(step);
        return this;
    }

    /// <summary>Build and return the configured <see cref="AutomationEnvelope"/>.</summary>
    public AutomationEnvelope Build() => new(
        SchemaVersion: _schemaVersion,
        AutomationId: _automationId,
        RuntimeType: _runtimeType,
        PolicyRefs: _policyRefs.Count > 0 ? _policyRefs.AsReadOnly() : null,
        Inputs: _inputs.Count > 0 ? _inputs : null,
        Outputs: _outputs.Count > 0 ? _outputs : null,
        Vars: _vars.Count > 0 ? _vars : null,
        Steps: _steps.AsReadOnly());
}
