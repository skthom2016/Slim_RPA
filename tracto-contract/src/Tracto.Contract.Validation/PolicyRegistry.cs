#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tracto.Contract.Validation;

public record PolicyRegistryEntry(
    [property: JsonPropertyName("policyRef")] string PolicyRef,
    [property: JsonPropertyName("permissions")] IReadOnlyList<string> Permissions,
    [property: JsonPropertyName("description")] string? Description = null);

public sealed class PolicyRegistry
{
    private readonly IReadOnlyDictionary<string, PolicyRegistryEntry> _entries;

    public PolicyRegistry(IReadOnlyList<PolicyRegistryEntry> entries)
    {
        _entries = entries.ToDictionary(e => e.PolicyRef, StringComparer.Ordinal);
    }

    public static PolicyRegistry FromJson(string json)
    {
        var entries = JsonSerializer.Deserialize<List<PolicyRegistryEntry>>(json)
            ?? throw new InvalidOperationException("Failed to deserialize policy registry.");
        return new PolicyRegistry(entries);
    }

    public bool Contains(string policyRef) => _entries.ContainsKey(policyRef);
    public IReadOnlyList<string> GetAllRefs() => _entries.Keys.ToList();
    public PolicyRegistryEntry? Get(string policyRef) => _entries.GetValueOrDefault(policyRef);
}
