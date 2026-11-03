using System.Text.Json;
using System.Text.Json.Serialization;

namespace TractoRuntime.Browser.Host;

/// <summary>
/// The flat Designer script format: an ordered list of steps whose parameters are all strings.
/// Published artifacts wrap it as <c>{"schemaVersion":"1","script":{...}}</c>; a bare script
/// without the wrapper is accepted too.
/// </summary>
public sealed class FlatScript
{
    [JsonPropertyName("id")]            public string? Id { get; set; }
    [JsonPropertyName("name")]          public string? Name { get; set; }
    [JsonPropertyName("version")]       public string? Version { get; set; }
    [JsonPropertyName("targetRuntime")] public string? TargetRuntime { get; set; }
    [JsonPropertyName("steps")]         public List<FlatStep> Steps { get; set; } = [];

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public static FlatScript Parse(byte[] raw)
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        var scriptElement = root.TryGetProperty("script", out var inner) && inner.ValueKind == JsonValueKind.Object
            ? inner
            : root;

        return scriptElement.Deserialize<FlatScript>(Json)
            ?? throw new ScriptValidationException("Artifact payload is not a script.");
    }
}

public sealed class FlatStep
{
    [JsonPropertyName("stepNumber")]  public int StepNumber { get; set; }
    [JsonPropertyName("action")]      public string Action { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("parameters")]  public Dictionary<string, string?> Parameters { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Raised when a script cannot be run as written. No browser is launched.</summary>
public sealed class ScriptValidationException(string message) : Exception(message);
