using System.Text.RegularExpressions;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.Interfaces;
using Tracto.TerminalRuntime.Domain.Policies;

namespace Tracto.TerminalRuntime.Infrastructure.Security;

/// <summary>
/// Production implementation of <see cref="ICommandTemplateSafetyValidator"/>.
///
/// <para>
/// Performs regex-based inspection of a command template string before any process
/// is launched.  Two independent checks run in sequence:
/// </para>
/// <list type="number">
///   <item>
///     <b>Business-predicate check</b> — rejects templates that contain numeric threshold
///     comparisons or domain-specific conditionals.  These belong in the Orchestrator's
///     decision layer, never in a shell command template.
///   </item>
///   <item>
///     <b>Network-tool check</b> — rejects templates that invoke shell-level HTTP or SQL
///     clients directly.  All external calls must route through <c>service.invoke</c>.
///   </item>
/// </list>
/// <para>
/// The validator also rejects templates whose text was assembled dynamically from an
/// external (API) response by checking whether the template contains characters that
/// indicate it was never a static string (e.g. JSON-value injection markers or
/// <see cref="DynamicInjectionPattern"/>).  Callers should pass the raw template
/// text exactly as authored; dynamic strings are rejected on that basis.
/// </para>
/// </summary>
public sealed class CommandTemplateSafetyValidator : ICommandTemplateSafetyValidator
{
    // ── Detail codes ────────────────────────────────────────────────────────────

    private const string BusinessLogicDetailCode    = "terminal.businessLogicInTemplate";
    private const string NetworkBypassDetailCode    = "terminal.networkBypassInTemplate";
    private const string DynamicCommandDetailCode   = "terminal.dynamicCommandRejected";

    /// <summary>
    /// Regex that flags a template as having been assembled from dynamic (API-response)
    /// data.  The pattern looks for common string-interpolation artifacts:
    /// <c>${...}</c>, <c>#{...}</c>, <c>{{...}}</c>, or raw JSON value injection
    /// sequences (<c>"key":"value"</c> fragments embedded in a shell string).
    /// </summary>
    private static readonly Regex DynamicInjectionPattern = new(
        @"(?:\$\{[^}]+\}|#\{[^}]+\}|\{\{[^}]+\}\}|""[^""]*""\s*:\s*""[^""]*"")",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    // ── ICommandTemplateSafetyValidator ─────────────────────────────────────────

    /// <inheritdoc/>
    public Result<string, Error> Validate(string templateText) =>
        Validate(templateText, CommandTemplateSafetyPolicy.Default);

    /// <inheritdoc/>
    public Result<string, Error> Validate(string templateText, CommandTemplateSafetyPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(templateText);
        ArgumentNullException.ThrowIfNull(policy);

        // ── Step 1: business-predicate patterns ──────────────────────────────
        foreach (var rawPattern in policy.ForbiddenPatterns)
        {
            if (string.IsNullOrWhiteSpace(rawPattern))
                continue;

            if (MatchesPattern(templateText, rawPattern, out var matchedBusiness))
            {
                return Result<string, Error>.Fail(
                    Error.Validation(
                        "terminal.commandTemplateSafety",
                        $"Command template contains a forbidden business-logic predicate " +
                        $"(matched: '{matchedBusiness}').  Business rules must not be " +
                        $"encoded in command templates.",
                        BusinessLogicDetailCode));
            }
        }

        // ── Step 2: forbidden network-tool names ─────────────────────────────
        foreach (var rawPattern in policy.ForbiddenNetworkTools)
        {
            if (string.IsNullOrWhiteSpace(rawPattern))
                continue;

            if (MatchesPattern(templateText, rawPattern, out var matchedNetwork))
            {
                return Result<string, Error>.Fail(
                    Error.Validation(
                        "terminal.commandTemplateSafety",
                        $"Command template contains a forbidden direct network-tool invocation " +
                        $"(matched: '{matchedNetwork}').  External API/database calls must use " +
                        $"service.invoke, not shell-level HTTP or SQL clients.",
                        NetworkBypassDetailCode));
            }
        }

        // ── Step 3: dynamic-injection guard ──────────────────────────────────
        if (DynamicInjectionPattern.IsMatch(templateText))
        {
            return Result<string, Error>.Fail(
                Error.Validation(
                    "terminal.commandTemplateSafety",
                    "Command template appears to contain dynamically injected content derived " +
                    "from an external source.  Only static, pre-authored templates are permitted.",
                    DynamicCommandDetailCode));
        }

        return Result<string, Error>.Ok(templateText);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to match <paramref name="pattern"/> against <paramref name="text"/>.
    /// Returns <c>true</c> and sets <paramref name="matched"/> to the matched value
    /// when a match is found; otherwise returns <c>false</c>.
    ///
    /// Any regex compilation or evaluation error is treated as a non-match so that a
    /// malformed policy pattern cannot crash the validator.
    /// </summary>
    private static bool MatchesPattern(string text, string pattern, out string matched)
    {
        matched = string.Empty;
        try
        {
            var regex = new Regex(pattern,
                RegexOptions.IgnoreCase | RegexOptions.Multiline,
                TimeSpan.FromSeconds(1));

            var m = regex.Match(text);
            if (m.Success)
            {
                matched = m.Value;
                return true;
            }

            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            // A timeout means we could not safely evaluate the pattern.
            // Fail-closed: treat as a match so the template is rejected.
            // A validator that can be bypassed via ReDoS breaks the security guarantee.
            matched = string.Empty;
            return true;
        }
        catch (ArgumentException)
        {
            // Invalid regex in policy — skip rather than crash.
            return false;
        }
    }
}
