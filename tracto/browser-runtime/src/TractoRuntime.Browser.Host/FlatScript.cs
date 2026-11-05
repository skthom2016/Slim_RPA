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

    /// <summary>
    /// Named profile this script runs on, so sites signed into earlier stay signed in. Null
    /// leaves the host's own BROWSER_PROFILE setting in charge, which is normally empty and
    /// means a throwaway browser.
    /// </summary>
    [JsonIgnore] public string? Profile { get; private set; }

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
        script.Profile = ReadProfile(scriptElement);
        return script;
    }

    private static string? ReadProfile(JsonElement script)
    {
        if (!script.TryGetProperty("profile", out var el) || el.ValueKind == JsonValueKind.Null)
            return null;
        if (el.ValueKind != JsonValueKind.String)
            throw new ScriptValidationException("'profile' must be the name of a browser profile, as text.");

        var name = el.GetString()?.Trim() ?? string.Empty;
        if (name.Length == 0) return null;
        if (!BrowserProfile.IsValidName(name))
            throw new ScriptValidationException(
                $"'profile' is \"{name}\"; use letters, digits, dash, underscore or dot (1 to 64 characters).");
        return name;
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
