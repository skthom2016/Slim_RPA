#nullable enable

using System.Text;
using System.Text.Json;

namespace Tracto.Desktop.Protocol;

/// <summary>
/// Describes in one line what a step that talked to a service got back, for the run's log: the
/// status code and the plain values of the reply, such as <c>statusCode=202; state=Completed;
/// total=0; message=nothing to do</c>.
/// </summary>
/// <remarks>
/// Only <c>http.get</c> and <c>http.post</c> are described. Other steps can hold what the person
/// running the flow does not expect to see in a log, such as clipboard text or a file's contents,
/// so their values are never copied into the log. The raw body is left out as well; its simple
/// top-level fields are already listed one by one. The Orchestrator also blanks anything that
/// looks like a credential before it stores a line.
/// </remarks>
public static class StepLogSummary
{
    private const int MaxFields = 12;
    private const int MaxValueChars = 160;

    // Keys that carry the whole reply rather than one field of it.
    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "body", "json", "text",
    };

    /// <summary>The action names whose reply is described.</summary>
    /// <param name="action">The step's action, such as <c>http.post</c>.</param>
    /// <returns>True when <see cref="Describe"/> returns something for this action.</returns>
    public static bool Describes(string? action) =>
        string.Equals(action, "http.get", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(action, "http.post", StringComparison.OrdinalIgnoreCase);

    /// <summary>One line describing the step's reply, or null when there is nothing to say.</summary>
    /// <param name="action">The step's action.</param>
    /// <param name="outputs">What the step returned.</param>
    /// <returns>Text like <c>statusCode=202; state=Running</c>, or null.</returns>
    public static string? Describe(string? action, IReadOnlyDictionary<string, object?>? outputs)
    {
        if (!Describes(action) || outputs is null || outputs.Count == 0) return null;

        var parts = new List<string>();
        var more = 0;

        // The status code first, then the reply's own fields in the order the service sent them.
        foreach (var key in outputs.Keys.OrderBy(k => string.Equals(k, "statusCode", StringComparison.OrdinalIgnoreCase) ? 0 : 1))
        {
            if (Skipped.Contains(key)) continue;
            var value = Plain(outputs[key]);
            if (value is null) continue;
            if (parts.Count >= MaxFields) { more++; continue; }
            parts.Add($"{key}={Clip(value)}");
        }
        if (more > 0) parts.Add($"(+{more} more)");

        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    // A simple value as text; objects, arrays and nothing at all are not described.
    private static string? Plain(object? value) => value switch
    {
        null => null,
        string s => s,
        bool b => b ? "true" : "false",
        JsonElement e => e.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
            ? e.ToString()
            : null,
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => null,
    };

    // One line of limited length: line breaks and runs of spaces become single spaces.
    private static string Clip(string value)
    {
        var sb = new StringBuilder(value.Length);
        var space = false;
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch)) { space = true; continue; }
            if (space && sb.Length > 0) sb.Append(' ');
            space = false;
            sb.Append(ch);
        }
        var text = sb.ToString();
        return text.Length <= MaxValueChars ? text : text[..MaxValueChars] + "…";
    }
}
