using System.Text.Json;
using System.Text.Json.Serialization;

namespace TractoRuntime.Browser.Binary;

/// <summary>
/// Loads a <see cref="BrowserBinaryManifest"/> from a JSON file on disk.
/// The manifest is typically shipped alongside the runtime executable as
/// <c>browser-manifest.json</c>, or embedded inside the isolated offline bundle.
///
/// The file is treated as immutable once loaded and its contents are validated
/// for schema version compatibility.
/// </summary>
public static class BrowserBinaryManifestLoader
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Loads the manifest from <paramref name="manifestPath"/>.
    /// </summary>
    /// <exception cref="BinaryValidationException">
    /// Thrown when the file is missing, malformed, or declares an unsupported schema version.
    /// </exception>
    public static async Task<BrowserBinaryManifest> LoadAsync(
        string manifestPath,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        if (!File.Exists(manifestPath))
        {
            throw new BinaryValidationException(
                $"Browser binary manifest not found at '{manifestPath}'. " +
                "The runtime package is incomplete.",
                "browser.notInstalled");
        }

        await using FileStream fs = new(
            manifestPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4_096,
            useAsync: true);

        BrowserBinaryManifest? manifest;
        try
        {
            manifest = await JsonSerializer.DeserializeAsync<BrowserBinaryManifest>(
                fs, s_jsonOptions, ct).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new BinaryValidationException(
                $"Browser binary manifest at '{manifestPath}' is malformed: {ex.Message}",
                "browser.notInstalled",
                ex);
        }

        if (manifest is null)
        {
            throw new BinaryValidationException(
                $"Browser binary manifest at '{manifestPath}' deserialized to null.",
                "browser.notInstalled");
        }

        if (!string.Equals(manifest.SchemaVersion, "1.0", StringComparison.Ordinal))
        {
            throw new BinaryValidationException(
                $"Unsupported browser manifest schema version '{manifest.SchemaVersion}'. " +
                "Expected '1.0'.",
                "browser.notInstalled");
        }

        if (manifest.Entries is null || manifest.Entries.Count == 0)
        {
            throw new BinaryValidationException(
                "Browser binary manifest contains no entries.",
                "browser.notInstalled");
        }

        return manifest;
    }

    /// <summary>
    /// Returns the default manifest path relative to the application base directory.
    /// Callers may override this with a path from deployment configuration.
    /// </summary>
    public static string DefaultManifestPath()
    {
        string baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, "browser-manifest.json");
    }
}
