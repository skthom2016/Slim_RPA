#nullable enable

using System.Text.Json;

namespace Tracto.Contract.Validation;

/// <summary>
/// In-memory catalogue of approved service operations.
/// Key: "serviceRef/operationRef" (ordinal).
/// </summary>
public sealed class ServiceRegistry
{
    // Key: "serviceRef/operationRef"
    private readonly IReadOnlyDictionary<string, ServiceRegistryEntry> _entries;

    public ServiceRegistry(IReadOnlyList<ServiceRegistryEntry> entries)
    {
        _entries = entries.ToDictionary(
            e => $"{e.ServiceRef}/{e.OperationRef}",
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Deserializes a JSON array of <see cref="ServiceRegistryEntry"/> objects.
    /// </summary>
    public static ServiceRegistry FromJson(string json)
    {
        var entries = JsonSerializer.Deserialize<List<ServiceRegistryEntry>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to deserialize service registry.");
        return new ServiceRegistry(entries);
    }

    public ServiceRegistryEntry? Find(string serviceRef, string operationRef)
        => _entries.GetValueOrDefault($"{serviceRef}/{operationRef}");

    public bool IsRegistered(string serviceRef, string operationRef)
        => Find(serviceRef, operationRef) is not null;

    public IReadOnlyList<string> GetRegisteredKeys() => _entries.Keys.ToList();
}
