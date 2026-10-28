#nullable enable

using System.Collections.Generic;
using System.Text.Json;

namespace Tracto.Contract.Execution;

public interface IVariableSpace
{
    // Returns all declared var names.
    IReadOnlyList<string> DeclaredVars { get; }

    // Returns true if the var has been written (assigned) at least once.
    bool IsAssigned(string varName);

    // Returns the current value. Throws VariableSpaceException(uninitializedRead) if unassigned.
    JsonElement Read(string varName);

    // Writes a value. Validates type compatibility against declaration.
    void Write(string varName, JsonElement value);
}
