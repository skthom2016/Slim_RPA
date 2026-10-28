#nullable enable

// Implements IVariableSpace for a single run attempt (C1 APPROVED).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Tracto.Contract.Models.Envelope;

namespace Tracto.Contract.Execution;

public sealed class VariableSpace : IVariableSpace
{
    private readonly IReadOnlyDictionary<string, VarDeclaration> _declarations;
    private readonly Dictionary<string, JsonElement> _values = new(StringComparer.Ordinal);

    public VariableSpace(IReadOnlyDictionary<string, VarDeclaration> declarations)
    {
        _declarations = declarations ?? throw new ArgumentNullException(nameof(declarations));
        // Initialize vars with defaultValue if declared
        foreach (var (name, decl) in declarations)
        {
            if (decl.DefaultValue.HasValue)
                _values[name] = decl.DefaultValue.Value;
        }
    }

    public IReadOnlyList<string> DeclaredVars => _declarations.Keys.ToList();

    public bool IsAssigned(string varName) => _values.ContainsKey(varName);

    public JsonElement Read(string varName)
    {
        if (!_declarations.ContainsKey(varName))
            throw new VariableSpaceException(VariableErrorCodes.UndeclaredReference, varName,
                $"Variable '{varName}' is not declared in the vars map.");
        if (!_values.TryGetValue(varName, out var value))
            throw new VariableSpaceException(VariableErrorCodes.UninitializedRead, varName,
                $"Variable '{varName}' has not been assigned. errorCategory=VALIDATION, detail=variable.uninitializedRead.");
        return value;
    }

    public void Write(string varName, JsonElement value)
    {
        if (!_declarations.TryGetValue(varName, out var decl))
            throw new VariableSpaceException(VariableErrorCodes.UndeclaredReference, varName,
                $"Variable '{varName}' is not declared.");
        // Type check
        ValidateType(varName, value, decl.Type, decl.Nullable ?? false);
        _values[varName] = value;
    }

    private static void ValidateType(string varName, JsonElement value, string declaredType, bool nullable)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            if (!nullable)
                throw new VariableSpaceException(VariableErrorCodes.TypeMismatch, varName,
                    $"Variable '{varName}' is non-nullable but received null. errorCategory=VALIDATION, detail=variable.typeMismatch.");
            return;
        }
        var ok = declaredType switch
        {
            "string"  => value.ValueKind == JsonValueKind.String,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            _ => false
        };
        if (!ok)
            throw new VariableSpaceException(VariableErrorCodes.TypeMismatch, varName,
                $"Variable '{varName}' expected type '{declaredType}' but received {value.ValueKind}. errorCategory=VALIDATION, detail=variable.typeMismatch.");
    }
}
