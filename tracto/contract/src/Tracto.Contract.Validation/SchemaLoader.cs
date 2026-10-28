using System.Reflection;
using System.Text.Json;
using Json.Schema;

namespace Tracto.Contract.Validation;

public sealed class SchemaLoader
{
    private static readonly JsonSerializerOptions CatalogueOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // Loads schema from embedded resource: "Tracto.Contract.Schemas.schemas.v1.<schemaFileName>"
    public static async Task<JsonSchema> LoadAsync(string schemaFileName, CancellationToken ct = default)
    {
        var schemasAssembly = Assembly.Load("Tracto.Contract.Schemas");
        var resourceName = $"Tracto.Contract.Schemas.schemas.v1.{schemaFileName}";
        using var stream = schemasAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Schema not found: {resourceName}");
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync(ct);
        return JsonSchema.FromText(json);
    }

    public static async Task<SchemaVersionCatalogue> LoadCatalogueAsync(CancellationToken ct = default)
    {
        var schemasAssembly = Assembly.Load("Tracto.Contract.Schemas");
        var resourceName = "Tracto.Contract.Schemas.schema-version-catalogue.json";
        using var stream = schemasAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Schema version catalogue not found.");
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync(ct);
        return JsonSerializer.Deserialize<SchemaVersionCatalogue>(json, CatalogueOptions)!;
    }
}
