using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tracto.TerminalRuntime.Domain;
using Tracto.TerminalRuntime.Infrastructure;

namespace Tracto.TerminalRuntime.Integration.Tests.CecConformance;

/// <summary>
/// CF-02 — Artifact Validation
///
/// A Terminal worker must reject an artifact before process launch when:
///   - The artifact signature is absent or wrong (unsigned artifact).
///   - The RuntimeType does not match "terminal" (wrong-runtime artifact).
///   - The HMAC key is not configured (misconfigured worker).
///
/// A correctly signed "terminal" artifact must be accepted.
/// </summary>
public sealed class CF02_ArtifactValidationTest : IClassFixture<MockOrchestratorFixture>
{
    private readonly MockOrchestratorFixture _fx;

    public CF02_ArtifactValidationTest(MockOrchestratorFixture fx) => _fx = fx;

    [Fact(DisplayName = "CF-02-A: Unsigned artifact is rejected")]
    public void UnsignedArtifact_IsRejected()
    {
        var unsigned = new ArtifactManifest("terminal", 1, ["terminal.run-script"], ["echo hi"], string.Empty);
        _fx.ArtifactValidator.IsSignatureValid(unsigned).Should().BeFalse(
            "an artifact with an empty signature must never be accepted");
    }

    [Fact(DisplayName = "CF-02-B: Wrong-runtime artifact is rejected by the full validation handler")]
    public void WrongRuntimeArtifact_IsRejectedByHandler()
    {
        // Build a "browser" manifest signed with the same HMAC key — the HMAC check
        // will PASS (because the key and algorithm are correct) but the handler must
        // then reject the artifact because RuntimeType != "terminal" (AC-2).
        var browserKey      = Convert.FromBase64String(MockOrchestratorFixture.TestHmacKeyBase64);
        var unsignedBrowser = new ArtifactManifest("browser", 1, ["browser.navigate"], ["nav https://example.com"], string.Empty);
        var sig             = ArtifactValidator.ComputeSignature(unsignedBrowser, browserKey);
        var signedBrowser   = unsignedBrowser with { Signature = sig };

        // The full validation handler checks RuntimeType AFTER HMAC verification (AC-2).
        var handler = new Tracto.TerminalRuntime.Application.ValidateArtifactCommandHandler(_fx.ArtifactValidator);
        var result  = handler.Handle(new Tracto.TerminalRuntime.Application.ValidateArtifactCommand(signedBrowser));

        result.IsFailure.Should().BeTrue(
            "an artifact whose RuntimeType is not 'terminal' must be rejected before any shell interaction");
        result.Error.Code.Should().Be("ARTIFACT_WRONG_RUNTIME");
    }

    [Fact(DisplayName = "CF-02-C: Tampered artifact is rejected")]
    public void TamperedArtifact_IsRejected()
    {
        var signed      = _fx.BuildSignedManifest();
        var tampered    = signed with { CommandRefs = ["rm -rf /"] };
        _fx.ArtifactValidator.IsSignatureValid(tampered).Should().BeFalse(
            "an artifact whose CommandRefs have been altered after signing must be rejected");
    }

    [Fact(DisplayName = "CF-02-D: Valid signed terminal artifact is accepted")]
    public void ValidSignedTerminalArtifact_IsAccepted()
    {
        var signed = _fx.BuildSignedManifest();
        _fx.ArtifactValidator.IsSignatureValid(signed).Should().BeTrue(
            "a correctly signed terminal artifact must be accepted");
    }

    [Fact(DisplayName = "CF-02-E: Unconfigured HMAC key rejects all artifacts")]
    public void MissingHmacKey_RejectsAllArtifacts()
    {
        var emptySettings  = Options.Create(new ArtifactSignatureSettings { HmacKeyBase64 = string.Empty });
        var validator      = new ArtifactValidator(emptySettings, NullLogger<ArtifactValidator>.Instance);
        var signed         = _fx.BuildSignedManifest();
        validator.IsSignatureValid(signed).Should().BeFalse(
            "when HmacKeyBase64 is not configured every artifact must be rejected");
    }

    [Fact(DisplayName = "CF-02-F: Wrong HMAC key rejects a correctly signed artifact")]
    public void WrongHmacKey_RejectsCorrectlySignedArtifact()
    {
        var differentKey   = "ZGlmZmVyZW50LWtleS0zMi1ieXRlcy0hISE="; // different 32-byte key
        var wrongSettings  = Options.Create(new ArtifactSignatureSettings { HmacKeyBase64 = differentKey });
        var validator      = new ArtifactValidator(wrongSettings, NullLogger<ArtifactValidator>.Instance);
        var signed         = _fx.BuildSignedManifest();
        validator.IsSignatureValid(signed).Should().BeFalse(
            "a validator configured with a different key must not accept a signature from the fixture key");
    }
}
