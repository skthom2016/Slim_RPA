#nullable enable

using System.Collections.Generic;
using System.Text.Json.Serialization;
using Tracto.Contract.Models.Protocol;

namespace Tracto.Contract.Models.Envelope;

public record AutomationEnvelope(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("automationId")] string AutomationId,
    [property: JsonPropertyName("runtimeType")] RuntimeType RuntimeType,
    [property: JsonPropertyName("policyRefs")] IReadOnlyList<string>? PolicyRefs,
    [property: JsonPropertyName("inputs")] IReadOnlyDictionary<string, InputDeclaration>? Inputs,
    [property: JsonPropertyName("outputs")] IReadOnlyDictionary<string, OutputDeclaration>? Outputs,
    [property: JsonPropertyName("vars")] IReadOnlyDictionary<string, VarDeclaration>? Vars,
    [property: JsonPropertyName("steps")] IReadOnlyList<StepDefinition> Steps);
