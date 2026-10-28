#nullable enable

using System.Text.Json;

namespace Tracto.Contract.Testing;

/// <summary>
/// Utility for loading JSON fixture files from disk into test assertions.
/// Keeps fixture loading consistent across all conformance test projects.
/// </summary>
public static class SchemaFixtureLoader
{
    /// <summary>
    /// Read a fixture file and return its raw JSON string.
    /// </summary>
    /// <param name="path">Absolute or relative path to the fixture file.</param>
    /// <exception cref="FileNotFoundException">
    /// Thrown when <paramref name="path"/> does not exist.
    /// </exception>
    public static string LoadFixture(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Fixture file not found: {path}", path);

        return File.ReadAllText(path);
    }

    /// <summary>
    /// Read a fixture file and parse it as a <see cref="JsonDocument"/>.
    /// Callers are responsible for disposing the returned document.
    /// </summary>
    /// <param name="path">Absolute or relative path to the fixture file.</param>
    /// <exception cref="FileNotFoundException">
    /// Thrown when <paramref name="path"/> does not exist.
    /// </exception>
    public static JsonDocument LoadFixtureAsDocument(string path)
        => JsonDocument.Parse(LoadFixture(path));
}
