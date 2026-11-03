using System.Text.Json;
using System.Text.Json.Serialization;
using TractoRuntime.Browser.Contracts;

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

    /// <summary>
    /// <c>headed</c> or <c>headless</c> for this script only. Null leaves the host's own
    /// BROWSER_MODE setting in charge.
    /// </summary>
    [JsonIgnore] public BrowserMode? Mode { get; private set; }

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public static FlatScript Parse(byte[] raw)
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        var scriptElement = root.TryGetProperty("script", out var inner) && inner.ValueKind == JsonValueKind.Object
            ? inner
            : root;

        var script = scriptElement.Deserialize<FlatScript>(Json)
            ?? throw new ScriptValidationException("Artifact payload is not a script.");

        script.Mode = ReadMode(scriptElement);
        return script;
    }

    private static BrowserMode? ReadMode(JsonElement script)
    {
        if (!script.TryGetProperty("mode", out var el) || el.ValueKind == JsonValueKind.Null)
            return null;
        if (el.ValueKind != JsonValueKind.String)
            throw new ScriptValidationException("'mode' must be the text \"headed\" or \"headless\".");

        var text = el.GetString()?.Trim() ?? string.Empty;
        if (text.Equals("headed", StringComparison.OrdinalIgnoreCase)) return BrowserMode.Headed;
        if (text.Equals("headless", StringComparison.OrdinalIgnoreCase)) return BrowserMode.Headless;
        throw new ScriptValidationException($"'mode' is \"{text}\"; it must be \"headed\" or \"headless\".");
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
