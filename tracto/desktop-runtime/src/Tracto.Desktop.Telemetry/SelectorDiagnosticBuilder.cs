using System.Text.RegularExpressions;
using Tracto.Desktop.Element;

namespace Tracto.Desktop.Telemetry;

/// <summary>
/// Builds a <see cref="SelectorDiagnostic"/> from a selector resolution attempt,
/// ensuring no secret values are exposed in the output.
/// </summary>
public static class SelectorDiagnosticBuilder
{
    // Email pattern — user@host form captures the user part which can be a secret.
    private static readonly Regex EmailPattern =
        new Regex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);

    // Home path: Windows C:\Users\<name> or Unix /home/<name>
    private static readonly Regex HomePathPattern =
        new Regex(@"(?:[Cc]:\\Users\\|/home/)[^\\/\s]+", RegexOptions.Compiled);

    /// <summary>
    /// Builds a <see cref="SelectorDiagnostic"/> for the given selector resolution attempt.
    /// </summary>
    /// <param name="sel">The selector that was applied.</param>
    /// <param name="candidateCount">Number of matching candidates found.</param>
    /// <param name="rawTitle">The raw window title at the time of the search.</param>
    /// <param name="activeSecretPresent">
    /// True when an active secret value is in scope; forces omission of any value
    /// that could reveal the secret.
    /// </param>
    public static SelectorDiagnostic Build(
        ElementSelector sel,
        int candidateCount,
        string rawTitle,
        bool activeSecretPresent)
    {
        ArgumentNullException.ThrowIfNull(sel);

        string strategy = DeriveStrategy(sel);

        string? sanitizedTitle = activeSecretPresent
            ? null   // Omit the title entirely when a secret is in scope.
            : SanitizeTitle(rawTitle);

        return new SelectorDiagnostic
        {
            Strategy           = strategy,
            Scope              = "Desktop",
            CandidateCount     = candidateCount,
            SanitizedWindowTitle = sanitizedTitle,
        };
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static string DeriveStrategy(ElementSelector sel)
    {
        if (sel.AutomationId is not null) return "AutomationId";
        if (sel.Name is not null)         return "Name";
        if (sel.ClassName is not null)    return "ClassName";
        if (sel.ControlType is not null)  return "ControlType";
        return "Unknown";
    }

    private static string? SanitizeTitle(string? rawTitle)
    {
        if (rawTitle is null) return null;

        // Strip email addresses.
        var result = EmailPattern.Replace(rawTitle, "[email]");

        // Strip home paths.
        result = HomePathPattern.Replace(result, "[homepath]");

        return result;
    }
}
