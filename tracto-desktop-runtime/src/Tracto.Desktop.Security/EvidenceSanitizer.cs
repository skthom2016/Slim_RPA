namespace Tracto.Desktop.Security;

/// <summary>
/// Sanitises evidence strings (logs, screenshots) by detecting and redacting known secret patterns.
/// </summary>
public static class EvidenceSanitizer
{
    private const string RedactionPlaceholder = "***";

    /// <summary>
    /// Returns <c>true</c> when <paramref name="text"/> contains any substring that matches
    /// <paramref name="knownPattern"/> using ordinal comparison.
    /// </summary>
    /// <param name="text">The text to inspect.</param>
    /// <param name="knownPattern">The pattern (literal substring) to search for.</param>
    public static bool ContainsPattern(string text, string knownPattern)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(knownPattern))
            return false;
        return text.Contains(knownPattern, StringComparison.Ordinal);
    }

    /// <summary>
    /// Replaces all occurrences of <paramref name="knownPattern"/> in <paramref name="text"/>
    /// with <c>"***"</c> using ordinal comparison.
    /// </summary>
    /// <param name="text">The text to sanitise.</param>
    /// <param name="knownPattern">The pattern (literal substring) to redact.</param>
    /// <returns>The sanitised string with all occurrences replaced.</returns>
    public static string Redact(string text, string knownPattern)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(knownPattern))
            return text;
        return text.Replace(knownPattern, RedactionPlaceholder, StringComparison.Ordinal);
    }
}
