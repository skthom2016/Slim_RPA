namespace Tracto.Designer.Core.Privacy;

using System.Text.RegularExpressions;

public static class SensitivePatternDetector
{
    private static readonly Regex Email = new(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", RegexOptions.Compiled);
    private static readonly Regex Phone = new(@"\b\d{10,}\b|\b\d{3}[-.\s]\d{3}[-.\s]\d{4}\b", RegexOptions.Compiled);
    private static readonly Regex Ssn   = new(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled);

    public const string Redacted = "[REDACTED]";

    public static bool ContainsPii(string? text, PrivacyPolicy policy)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (policy.HasFlag(PrivacyPolicy.RedactEmail) && Email.IsMatch(text)) return true;
        if (policy.HasFlag(PrivacyPolicy.RedactPhone) && Phone.IsMatch(text)) return true;
        if (policy.HasFlag(PrivacyPolicy.RedactSsn)   && Ssn.IsMatch(text))   return true;
        return false;
    }

    public static string Scrub(string? text, PrivacyPolicy policy)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var result = text;
        if (policy.HasFlag(PrivacyPolicy.RedactEmail)) result = Email.Replace(result, Redacted);
        if (policy.HasFlag(PrivacyPolicy.RedactPhone)) result = Phone.Replace(result, Redacted);
        if (policy.HasFlag(PrivacyPolicy.RedactSsn))   result = Ssn.Replace(result, Redacted);
        return result;
    }
}
