using System.Text.RegularExpressions;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.Redaction;
using Tracto.TerminalRuntime.Domain.Redaction;

namespace Tracto.TerminalRuntime.Infrastructure.Redaction;

/// <summary>
/// Default implementation of <see cref="IOutputRedactor"/>.
///
/// <para>
/// The redactor operates in two passes:
/// <list type="number">
///   <item>
///     <term>Token pass</term>
///     <description>
///       Every string in <see cref="OutputRedactionPolicy.ActiveSecretTokens"/> that is non-empty
///       and longer than one character is replaced with <see cref="Redacted"/> using a
///       plain ordinal comparison.  Short tokens (≤ 1 char) are skipped to avoid
///       pathological replacement of single-character output.
///     </description>
///   </item>
///   <item>
///     <term>Heuristic pass (MaskAll only)</term>
///     <description>
///       Matches any whitespace-delimited word whose length exceeds 8 characters AND
///       contains both upper- and lower-case letters AND at least one digit.  Such
///       tokens are statistically very likely to be machine-generated secrets and are
///       replaced with <see cref="Redacted"/>.
///     </description>
///   </item>
/// </list>
/// </para>
///
/// <para>
/// The implementation is intentionally conservative: false positives (redacting a
/// non-secret) are acceptable; false negatives (leaking a secret) are not.
/// </para>
/// </summary>
public sealed partial class OutputRedactor : IOutputRedactor
{
    public const string Redacted = "***REDACTED***";

    // Matches a word that looks like a machine-generated secret:
    //   • length > 8  (\S{9,})
    //   • contains at least one uppercase letter
    //   • contains at least one lowercase letter
    //   • contains at least one digit
    // The lookaheads enforce all three conditions without consuming characters.
    [GeneratedRegex(
        @"(?=\S*[A-Z])(?=\S*[a-z])(?=\S*\d)\S{9,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex SecretLikePattern();

    // ── IOutputRedactor ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Result<RedactedOutput, Error> Redact(string rawOutput, OutputRedactionPolicy policy)
    {
        var policyCheck = ValidatePolicy(policy);
        if (policyCheck.IsFailure)
            return Result<RedactedOutput, Error>.Fail(policyCheck.Error);

        if (string.IsNullOrEmpty(rawOutput))
            return Result<RedactedOutput, Error>.Ok(new RedactedOutput(string.Empty, false));

        var text = rawOutput;
        var redacted = false;

        // Pass 1 — replace known secret token values.
        foreach (var token in policy.ActiveSecretTokens)
        {
            if (string.IsNullOrEmpty(token) || token.Length <= 1)
                continue;

            if (text.Contains(token, StringComparison.Ordinal))
            {
                text = text.Replace(token, Redacted, StringComparison.Ordinal);
                redacted = true;
            }
        }

        // Pass 2 — heuristic scan (MaskAll only).
        if (policy.Mode == RedactionMode.MaskAll)
        {
            var replaced = SecretLikePattern().Replace(text, Redacted);
            if (!ReferenceEquals(replaced, text))
            {
                redacted = true;
                text = replaced;
            }
        }

        return Result<RedactedOutput, Error>.Ok(new RedactedOutput(text, redacted));
    }

    /// <inheritdoc/>
    public Result<bool, Error> ValidatePolicy(OutputRedactionPolicy policy)
    {
        // The spec states: disabling redaction entirely is NOT permitted.
        // There is no "None" mode in the domain enum, so every enum value is legal.
        // The guard here protects against a future out-of-range cast.
        if (!Enum.IsDefined(policy.Mode))
        {
            return Result<bool, Error>.Fail(Error.PolicyDenied(
                "REDACTION_POLICY_INVALID",
                $"OutputRedactionPolicy.Mode value '{(int)policy.Mode}' is not a recognised RedactionMode. " +
                "Disabling output redaction entirely is not permitted."));
        }

        return Result<bool, Error>.Ok(true);
    }
}
