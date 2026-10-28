using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.Stdin;
using Tracto.TerminalRuntime.Domain.Stdin;

namespace Tracto.TerminalRuntime.Infrastructure.Stdin;

/// <summary>
/// Monitors a process's stdout for interactive prompts and responds via stdin.
///
/// Algorithm (per stdout line, within <see cref="StdinPolicy.PromptTimeoutMs"/>):
/// <list type="number">
///   <item>Check the line against the ordered <see cref="PromptAllowlistEntry"/> list.
///         First match → write the <see cref="PromptAllowlistEntry.ApprovedResponse"/> and return Ok.</item>
///   <item>If not allowlisted but the line matches the internal credential-pattern detector
///         (password|token|secret|…), refuse immediately — no input is sent to the process
///         (AC-4). Returns PolicyDenied / terminal.credentialPromptDenied.</item>
///   <item>If neither condition is met, continue reading until EOF or timeout.</item>
///   <item>On timeout return TIMEOUT / terminal.promptTimeout (AC-3).</item>
/// </list>
///
/// Security invariants (CEC v1.0.1 #4): approved responses are NEVER written to logs.
/// </summary>
public sealed class PromptMatcher : IPromptMatcher
{
    /// <summary>
    /// Detects stdout lines that appear to be requesting a credential.
    /// Matches common keywords used by CLI password/token prompts.
    /// </summary>
    private static readonly Regex CredentialPromptPattern = new(
        @"\b(password|passwd|token|secret|credential|passphrase|api[\s_\-]?key|private[\s_]?key)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        matchTimeout: TimeSpan.FromMilliseconds(500));

    private const string CredentialDeniedDetailCode = "terminal.credentialPromptDenied";
    private const string PromptTimeoutDetailCode = "terminal.promptTimeout";

    private readonly IStdinWriter _stdinWriter;
    private readonly ILogger<PromptMatcher> _logger;

    public PromptMatcher(IStdinWriter stdinWriter, ILogger<PromptMatcher> logger)
    {
        _stdinWriter = stdinWriter ?? throw new ArgumentNullException(nameof(stdinWriter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<Result<bool, Error>> WaitForPromptAndRespondAsync(
        TextReader stdoutReader,
        Stream stdinStream,
        IReadOnlyList<PromptAllowlistEntry> allowlist,
        StdinPolicy policy,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stdoutReader);
        ArgumentNullException.ThrowIfNull(stdinStream);
        ArgumentNullException.ThrowIfNull(allowlist);
        ArgumentNullException.ThrowIfNull(policy);

        // Create a linked CTS that fires after PromptTimeoutMs (AC-2, AC-3).
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(policy.PromptTimeoutMs);

        try
        {
            while (true)
            {
                string? line;
                try
                {
                    line = await stdoutReader.ReadLineAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // External cancellation — propagate.
                    throw;
                }
                catch (OperationCanceledException)
                {
                    // Internal timeout CTS fired — fall through to TIMEOUT result.
                    break;
                }

                if (line is null)
                {
                    // EOF on stdout before a prompt was matched.
                    break;
                }

                // ── Step 1: Check the allowlist (AC-2) ──────────────────────────
                if (TryMatchAllowlist(line, allowlist, out string? approvedResponse))
                {
                    _logger.LogDebug(
                        "Allowlisted prompt matched on stdout; writing approved response ({ByteHint} chars).",
                        approvedResponse!.Length);

                    // Value is forwarded to IStdinWriter which never logs it (CEC #4).
                    return await _stdinWriter.WriteAsync(stdinStream, approvedResponse!, policy, ct);
                }

                // ── Step 2: Credential-pattern guard (AC-4) ─────────────────────
                // If the prompt looks like it is requesting a credential AND it was
                // not explicitly allowlisted above, the runtime refuses to respond.
                bool isCredentialLike;
                try
                {
                    isCredentialLike = CredentialPromptPattern.IsMatch(line);
                }
                catch (RegexMatchTimeoutException)
                {
                    // Pathological line — treat conservatively as a possible credential.
                    isCredentialLike = true;
                }

                if (isCredentialLike)
                {
                    _logger.LogWarning(
                        "A credential-like prompt was detected on process stdout but is not present " +
                        "in the approved allowlist.  No stdin input will be sent (AC-4).");

                    return Result<bool, Error>.Fail(
                        Error.PolicyDenied(
                            "STDIN_POLICY_003",
                            "A prompt that appears to request a credential was detected but is not " +
                            "in the approved allowlist.  The runtime will not respond to unapproved credential prompts.",
                            CredentialDeniedDetailCode));
                }

                // Line was neither allowlisted nor credential-like — continue reading.
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // External cancellation — propagate upward.
            throw;
        }

        // AC-3: No allowlisted prompt matched within the timeout window.
        _logger.LogWarning(
            "No allowlisted interactive prompt was matched within the {TimeoutMs} ms window.",
            policy.PromptTimeoutMs);

        return Result<bool, Error>.Fail(
            Error.Timeout(
                "STDIN_POLICY_004",
                $"No allowlisted interactive prompt was detected within {policy.PromptTimeoutMs} ms.",
                PromptTimeoutDetailCode));
    }

    /// <summary>
    /// Evaluates each <see cref="PromptAllowlistEntry"/> in order against <paramref name="line"/>.
    /// Returns true and sets <paramref name="approvedResponse"/> when the first match is found.
    /// </summary>
    private static bool TryMatchAllowlist(
        string line,
        IReadOnlyList<PromptAllowlistEntry> allowlist,
        out string? approvedResponse)
    {
        foreach (PromptAllowlistEntry entry in allowlist)
        {
            bool matched;
            try
            {
                matched = Regex.IsMatch(
                    line,
                    entry.Pattern,
                    RegexOptions.IgnoreCase,
                    matchTimeout: TimeSpan.FromMilliseconds(500));
            }
            catch (RegexMatchTimeoutException)
            {
                // Pathological input — treat as no-match and continue to next entry.
                matched = false;
            }
            catch (ArgumentException)
            {
                // Invalid pattern stored in the entry — skip safely.
                matched = false;
            }

            if (matched)
            {
                approvedResponse = entry.ApprovedResponse;
                return true;
            }
        }

        approvedResponse = null;
        return false;
    }
}
