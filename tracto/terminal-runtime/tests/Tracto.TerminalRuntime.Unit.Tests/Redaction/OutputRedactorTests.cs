using FluentAssertions;
using Tracto.TerminalRuntime.Domain.Redaction;
using Tracto.TerminalRuntime.Infrastructure.Redaction;
using Xunit;

namespace Tracto.TerminalRuntime.Unit.Tests.Redaction;

/// <summary>
/// Unit tests for TER-US-013 — Output Redaction and Secret Protection.
/// Each test maps to one or more acceptance criteria (AC-x).
/// </summary>
public sealed class OutputRedactorTests
{
    private readonly OutputRedactor _sut = new();

    // ── AC-1: Secret value absent from stdout / stderr after redaction ─────────

    [Fact]
    public void Redact_SecretToken_NotPresentInRedactedOutput_AC1()
    {
        // arrange
        const string secret = "SuperSecret123!";
        const string raw = $"Connecting with token={secret} to endpoint";

        var policy = new OutputRedactionPolicy(
            RedactionMode.MaskSecrets,
            new[] { secret });

        // act
        var result = _sut.Redact(raw, policy);

        // assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Content.Should().NotContain(secret);
        result.Value.Content.Should().Contain(OutputRedactor.Redacted);
        result.Value.WasRedacted.Should().BeTrue();
    }

    [Fact]
    public void Redact_MultipleSecretTokens_AllAbsentFromOutput_AC1()
    {
        // arrange
        const string token1 = "ApiKey_AbcDef123";
        const string token2 = "Bearer_XyzUvw456";
        var raw = $"key={token1} auth={token2} result=ok";

        var policy = new OutputRedactionPolicy(
            RedactionMode.MaskSecrets,
            new[] { token1, token2 });

        // act
        var result = _sut.Redact(raw, policy);

        // assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Content.Should().NotContain(token1);
        result.Value.Content.Should().NotContain(token2);
    }

    // ── AC-2: outputRedaction — MaskAll cannot be disabled ────────────────────

    [Fact]
    public void ValidatePolicy_WithValidMaskAllMode_Succeeds_AC2()
    {
        var policy = OutputRedactionPolicy.Default; // MaskAll
        var result = _sut.ValidatePolicy(policy);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidatePolicy_WithValidMaskSecretsMode_Succeeds_AC2()
    {
        var policy = new OutputRedactionPolicy(RedactionMode.MaskSecrets, Array.Empty<string>());
        var result = _sut.ValidatePolicy(policy);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidatePolicy_WithOutOfRangeMode_ReturnsPolicyDeniedError_AC2()
    {
        // Cast an integer outside the enum definition to simulate "disabled" mode.
        var policy = new OutputRedactionPolicy((RedactionMode)999, Array.Empty<string>());
        var result = _sut.ValidatePolicy(policy);

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(Application.Common.ErrorKind.PolicyDenied);
        result.Error.Code.Should().Be("REDACTION_POLICY_INVALID");
    }

    [Fact]
    public void Redact_WithInvalidPolicy_PropagatesError_AC2()
    {
        var policy = new OutputRedactionPolicy((RedactionMode)999, Array.Empty<string>());
        var result = _sut.Redact("some output", policy);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("REDACTION_POLICY_INVALID");
    }

    // ── AC-3: Audit record does NOT contain raw secret value ──────────────────

    [Fact]
    public void Redact_SecretUsedAsArgument_AbsentFromRedactedAuditPayload_AC3()
    {
        // Simulate audit record constructed from the command argument
        const string secret = "Passw0rd!Secret99";
        var simulatedAuditText = $"Executed: script.sh --password {secret} --verbose";

        var policy = new OutputRedactionPolicy(
            RedactionMode.MaskSecrets,
            new[] { secret });

        var result = _sut.Redact(simulatedAuditText, policy);

        result.IsSuccess.Should().BeTrue();
        result.Value.Content.Should().NotContain(secret);
        result.Value.WasRedacted.Should().BeTrue();
    }

    // ── AC-4: Sanitised evidence artifact from failed run has no live token ───

    [Fact]
    public void Redact_EvidenceFromFailedRun_ContainsNoLiveSessionToken_AC4()
    {
        // Simulate what evidence might look like after a failed run
        const string sessionToken = "eyJhbGciSession9xTkn";  // > 8 chars, mixed-case + digit
        var evidenceText =
            $"Run FAILED at 2026-10-24T00:00:00Z\n" +
            $"Authorization: Bearer {sessionToken}\n" +
            $"ExitCode: 1";

        var policy = new OutputRedactionPolicy(
            RedactionMode.MaskSecrets,
            new[] { sessionToken });

        var result = _sut.Redact(evidenceText, policy);

        result.IsSuccess.Should().BeTrue();
        result.Value.Content.Should().NotContain(sessionToken);
    }

    [Fact]
    public void Redact_MaskAllMode_HeuristicCatchesUnregisteredSecret_AC4()
    {
        // A token that was NOT added to ActiveSecretTokens but looks like a secret.
        // MaskAll should catch it via the heuristic.
        const string unknownToken = "Xy9zA1bC2dE3fG4h"; // len=16, mixed + digits
        var raw = $"output: {unknownToken}";

        var policy = new OutputRedactionPolicy(
            RedactionMode.MaskAll,
            Array.Empty<string>()); // deliberately no tokens

        var result = _sut.Redact(raw, policy);

        result.IsSuccess.Should().BeTrue();
        result.Value.Content.Should().NotContain(unknownToken);
        result.Value.WasRedacted.Should().BeTrue();
    }

    // ── Edge-case / boundary tests ─────────────────────────────────────────────

    [Fact]
    public void Redact_EmptyOutput_ReturnsEmptyNotRedacted()
    {
        var policy = OutputRedactionPolicy.Default;
        var result = _sut.Redact(string.Empty, policy);

        result.IsSuccess.Should().BeTrue();
        result.Value.Content.Should().BeEmpty();
        result.Value.WasRedacted.Should().BeFalse();
    }

    [Fact]
    public void Redact_OutputWithNoSecrets_ReturnsUnchanged()
    {
        const string raw = "Hello world 123";
        var policy = new OutputRedactionPolicy(RedactionMode.MaskSecrets, Array.Empty<string>());

        var result = _sut.Redact(raw, policy);

        result.IsSuccess.Should().BeTrue();
        result.Value.Content.Should().Be(raw);
        result.Value.WasRedacted.Should().BeFalse();
    }

    [Fact]
    public void Redact_ShortToken_IsSkipped_ToAvoidPathologicalReplacement()
    {
        // Single-char and two-char tokens are skipped.
        const string raw = "a=b value=x";
        var policy = new OutputRedactionPolicy(
            RedactionMode.MaskSecrets,
            new[] { "a", "b" });

        var result = _sut.Redact(raw, policy);

        // Length <= 1 are skipped; no replacement.
        result.IsSuccess.Should().BeTrue();
        result.Value.Content.Should().Be(raw);
    }

    [Fact]
    public void Redact_MaskSecretsMode_DoesNotApplyHeuristic()
    {
        // A string that looks secret-like but is NOT in ActiveSecretTokens.
        // MaskSecrets mode should NOT redact it.
        const string secretLike = "Xy9zA1bC2dE3fG4h";
        var raw = $"output: {secretLike}";

        var policy = new OutputRedactionPolicy(
            RedactionMode.MaskSecrets,
            Array.Empty<string>()); // no tokens

        var result = _sut.Redact(raw, policy);

        result.IsSuccess.Should().BeTrue();
        // MaskSecrets only replaces known tokens — heuristic not applied.
        result.Value.Content.Should().Be(raw);
        result.Value.WasRedacted.Should().BeFalse();
    }

    [Fact]
    public void Redact_DefaultPolicy_IsMaskAll()
    {
        OutputRedactionPolicy.Default.Mode.Should().Be(RedactionMode.MaskAll);
    }
}
