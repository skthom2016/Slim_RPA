namespace TractoRuntime.Browser.Configuration;

/// <summary>
/// Loads <c>KEY=VALUE</c> settings from a <c>.env</c> file into the process environment, so a
/// worker's secrets live in a file beside it rather than in a shell that has to be set up by
/// hand before every start. Matches the sheet-service loader so both read the same files.
/// </summary>
public static class EnvFile
{
    /// <summary>
    /// Reads <paramref name="path"/> and sets every variable that is not already present in the
    /// environment, so a real environment variable always wins over the file. Returns whether
    /// the file was found; a missing file is not an error.
    /// </summary>
    /// <remarks>
    /// Lines are <c>KEY=VALUE</c>. Blank lines and lines beginning with <c>#</c> are ignored, an
    /// optional <c>export </c> prefix is allowed, and a value wrapped in matching single or
    /// double quotes is unwrapped. Everything after the first <c>=</c> is the value, so a
    /// password containing <c>=</c> or <c>#</c> survives intact.
    /// </remarks>
    public static bool Load(string path)
    {
        if (!File.Exists(path)) return false;

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.TrimStart('﻿').Trim();
            if (line.StartsWith("export ", StringComparison.Ordinal))
                line = line["export ".Length..].Trim();

            if (line.Length == 0 || line[0] == '#') continue;

            var split = line.IndexOf('=');
            if (split <= 0) continue;

            var key = line[..split].Trim();
            if (key.Length == 0) continue;
            if (System.Environment.GetEnvironmentVariable(key) is not null) continue;

            System.Environment.SetEnvironmentVariable(key, Unquote(line[(split + 1)..].Trim()));
        }

        return true;
    }

    /// <summary>
    /// Loads each path in turn, so a file beside the executable can be overridden by one in the
    /// folder the worker was started from. Two paths naming the same file are read once, which
    /// is the normal case when the worker is started from its own output folder. Returns the
    /// full paths of the files that existed, in the order read.
    /// </summary>
    public static IReadOnlyList<string> LoadAll(params string[] paths)
    {
        var loaded = new List<string>();
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in paths)
        {
            var full = Path.GetFullPath(p);
            if (seen.Add(full) && Load(full))
                loaded.Add(full);
        }
        return loaded;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0]
            ? value[1..^1]
            : value;
}
