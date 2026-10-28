using FluentAssertions;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Validation;

namespace TractoRuntime.Browser.Tests.Validation;

/// <summary>
/// Unit tests for <see cref="ArtifactValidator"/>.
/// Covers BRW-AT-21: Invalid artifact / mixed-engine — an unsigned, tampered,
/// wrong-runtime or mixed desktop.* + browser.* artifact is rejected before launch
/// or site request, with expected VALIDATION or POLICY_DENIED category and non-secret detail.
/// Also covers BRW-US-017 AC#1–#5.
/// </summary>
public sealed class ArtifactValidatorTests
{
    private readonly ArtifactValidator _sut = new();

    // ─── Helpers ────────────────────────────────────────────────────────────────

    private static ArtifactEnvelope ValidEnvelope(
        IReadOnlyList<BrowserStep>? steps = null,
        bool allowCss = false)
        => new()
        {
            RuntimeType = "browser",
            SchemaMajor = "1",
            Signature = "sha256:aaabbbccc111222333",
            ArtifactDigest = "sha256:dddeeefff444555666",
            AllowCss = allowCss,
            Script = new BrowserScript
            {
                ScriptId = "test-script",
                Version = "1.0.0",
                Steps = steps ?? [new BrowserStep { StepId = "s1", Action = "browser.navigate" }]
            }
        };

    // ─── AC#1: Unsigned artifact rejected (BRW-US-017) ──────────────────────────

    [Fact]
    public void Validate_UnsignedArtifact_EmptySignature_ReturnsValidationError()
    {
        var envelope = ValidEnvelope() with { Signature = "" };

        var result = _sut.Validate(envelope);

        result.IsValid.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.VALIDATION);
        result.DetailCode.Should().Be("browser.artifactUnsigned");
    }

    [Fact]
    public void Validate_UnsignedArtifact_WhitespaceSignature_ReturnsValidationError()
    {
        var envelope = ValidEnvelope() with { Signature = "   " };

        var result = _sut.Validate(envelope);

        result.IsValid.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.VALIDATION);
        result.DetailCode.Should().Be("browser.artifactUnsigned");
    }

    [Fact]
    public void Validate_UnsignedArtifact_EmptyDigest_ReturnsValidationError()
    {
        var envelope = ValidEnvelope() with { ArtifactDigest = "" };

        var result = _sut.Validate(envelope);

        result.IsValid.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.VALIDATION);
        result.DetailCode.Should().Be("browser.artifactUnsigned");
    }

    [Fact]
    public void Validate_UnsignedArtifact_MessageDoesNotContainSecretValues()
    {
        var envelope = ValidEnvelope() with { Signature = "" };

        var result = _sut.Validate(envelope);

        // Message must not contain secret values — only structural description
        result.Message.Should().NotBeNullOrEmpty();
        result.Message!.ToLowerInvariant().Should().NotContain("password");
        result.Message.ToLowerInvariant().Should().NotContain("token");
        result.Message.ToLowerInvariant().Should().NotContain("secret");
    }

    // ─── AC#2: Wrong runtimeType rejected (BRW-US-017) ──────────────────────────

    [Fact]
    public void Validate_DesktopRuntimeType_ReturnsValidationError()
    {
        // BRW-AT-21: wrong-runtime artifact rejected before any browser interaction
        var envelope = ValidEnvelope() with { RuntimeType = "desktop" };

        var result = _sut.Validate(envelope);

        result.IsValid.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.VALIDATION);
        result.DetailCode.Should().Be("browser.artifactTypeMismatch");
    }

    [Fact]
    public void Validate_UnknownRuntimeType_ReturnsValidationError()
    {
        var envelope = ValidEnvelope() with { RuntimeType = "shell" };

        var result = _sut.Validate(envelope);

        result.IsValid.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.VALIDATION);
        result.DetailCode.Should().Be("browser.artifactTypeMismatch");
    }

    [Fact]
    public void Validate_BrowserRuntimeType_IsCaseInsensitive_PassesCheck()
    {
        var envelope = ValidEnvelope() with { RuntimeType = "BROWSER" };

        var result = _sut.Validate(envelope);

        // runtimeType check is case-insensitive; "BROWSER" is valid
        result.IsValid.Should().BeTrue();
    }

    // ─── AC#4: Unsupported schema major rejected (BRW-US-017) ──────────────────

    [Fact]
    public void Validate_UnsupportedSchemaMajor_V2_ReturnsValidationError()
    {
        var envelope = ValidEnvelope() with { SchemaMajor = "2" };

        var result = _sut.Validate(envelope);

        result.IsValid.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.VALIDATION);
        result.DetailCode.Should().Be("browser.schemaMajorUnsupported");
    }

    [Fact]
    public void Validate_SupportedSchemaMajor_V1_Passes()
    {
        var envelope = ValidEnvelope() with { SchemaMajor = "1" };

        var result = _sut.Validate(envelope);

        result.IsValid.Should().BeTrue();
    }

    // ─── AC#3: Mixed desktop.* + browser.* rejected (BRW-US-017) ───────────────

    [Fact]
    public void Validate_MixedDesktopAndBrowserActions_ReturnsValidationError()
    {
        // BRW-AT-21: mixed desktop.* + browser.* artifact rejected at schema validation
        var steps = new List<BrowserStep>
        {
            new() { StepId = "s1", Action = "browser.navigate" },
            new() { StepId = "s2", Action = "desktop.click" }
        };
        var envelope = ValidEnvelope(steps);

        var result = _sut.Validate(envelope);

        result.IsValid.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.VALIDATION);
        result.DetailCode.Should().Be("browser.mixedEngineScript");
        result.Message.Should().Contain("mixed");
    }

    [Fact]
    public void Validate_DesktopOnlyActions_ReturnsValidationError()
    {
        // BRW-AT-21: wrong-engine artifact rejected before any browser action
        var steps = new List<BrowserStep>
        {
            new() { StepId = "s1", Action = "desktop.navigate" },
            new() { StepId = "s2", Action = "desktop.click" }
        };
        var envelope = ValidEnvelope(steps);

        var result = _sut.Validate(envelope);

        result.IsValid.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.VALIDATION);
        result.DetailCode.Should().Be("browser.mixedEngineScript");
    }

    // ─── AC#5: Closed-behavior violations rejected (BRW-US-017) ────────────────

    [Fact]
    public void Validate_PageEvaluateAction_ReturnsPolicyDenied()
    {
        // BRW-US-017 AC#5 / BRW-AT-26: page.evaluate rejected before any browser side effect
        var steps = new List<BrowserStep>
        {
            new() { StepId = "s1", Action = "browser.navigate" },
            new() { StepId = "s2", Action = "page.evaluate" }
        };
        var envelope = ValidEnvelope(steps);

        var result = _sut.Validate(envelope);

        result.IsValid.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.POLICY_DENIED);
        result.DetailCode.Should().Be("browser.evaluationDenied");
    }

    [Fact]
    public void Validate_BrowserEvaluateAction_ReturnsPolicyDenied()
    {
        var steps = new List<BrowserStep>
        {
            new() { StepId = "s1", Action = "browser.evaluate" }
        };
        var envelope = ValidEnvelope(steps);

        var result = _sut.Validate(envelope);

        result.IsValid.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.POLICY_DENIED);
        result.DetailCode.Should().Be("browser.evaluationDenied");
    }

    [Fact]
    public void Validate_BrowserApiRequestAction_ReturnsPolicyDenied()
    {
        // BRW-AT-26: APIRequest targeting external business API rejected
        var steps = new List<BrowserStep>
        {
            new() { StepId = "s1", Action = "browser.api.request" }
        };
        var envelope = ValidEnvelope(steps);

        var result = _sut.Validate(envelope);

        result.IsValid.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.POLICY_DENIED);
        result.DetailCode.Should().Be("browser.evaluationDenied");
    }

    [Fact]
    public void Validate_BrowserFetchAction_ReturnsPolicyDenied()
    {
        var steps = new List<BrowserStep>
        {
            new() { StepId = "s1", Action = "browser.fetch" }
        };
        var envelope = ValidEnvelope(steps);

        var result = _sut.Validate(envelope);

        result.IsValid.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.POLICY_DENIED);
        result.DetailCode.Should().Be("browser.evaluationDenied");
    }

    // ─── CSS selector policy (F-08, normative selector schema) ─────────────────

    [Fact]
    public void Validate_CssSelector_WithoutAllowCssPolicy_ReturnsValidationError()
    {
        var steps = new List<BrowserStep>
        {
            new()
            {
                StepId = "s1",
                Action = "browser.click",
                Selector = new SelectorRef { Css = "button[data-testid='submit']" }
            }
        };
        var envelope = ValidEnvelope(steps, allowCss: false);

        var result = _sut.Validate(envelope);

        result.IsValid.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.VALIDATION);
        result.DetailCode.Should().Be("browser.cssNotAllowed");
    }

    [Fact]
    public void Validate_CssSelector_WithAllowCssPolicy_Passes()
    {
        var steps = new List<BrowserStep>
        {
            new()
            {
                StepId = "s1",
                Action = "browser.click",
                Selector = new SelectorRef { Css = "button.submit" }
            }
        };
        var envelope = ValidEnvelope(steps, allowCss: true);

        var result = _sut.Validate(envelope);

        result.IsValid.Should().BeTrue();
    }

    // ─── Happy-path: fully valid artifact ────────────────────────────────────

    [Fact]
    public void Validate_ValidArtifact_NoSteps_Passes()
    {
        var envelope = ValidEnvelope([]);

        var result = _sut.Validate(envelope);

        result.IsValid.Should().BeTrue();
        result.ErrorCategory.Should().BeNull();
        result.DetailCode.Should().BeNull();
    }

    [Fact]
    public void Validate_ValidArtifact_BrowserStepsOnly_Passes()
    {
        var steps = new List<BrowserStep>
        {
            new() { StepId = "s1", Action = "browser.navigate" },
            new() { StepId = "s2", Action = "browser.click" },
            new() { StepId = "s3", Action = "browser.fill" }
        };
        var envelope = ValidEnvelope(steps);

        var result = _sut.Validate(envelope);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ValidArtifact_SemanticSelectorNoCSS_Passes()
    {
        var steps = new List<BrowserStep>
        {
            new()
            {
                StepId = "s1",
                Action = "browser.click",
                Selector = new SelectorRef { AriaLabel = "Submit" }
            }
        };
        var envelope = ValidEnvelope(steps, allowCss: false);

        var result = _sut.Validate(envelope);

        result.IsValid.Should().BeTrue();
    }

    // ─── Check ordering: signature failure before runtimeType failure ────────────

    [Fact]
    public void Validate_UnsignedAndWrongRuntime_ReportsSignatureErrorFirst()
    {
        var envelope = ValidEnvelope() with
        {
            Signature = "",
            RuntimeType = "desktop"
        };

        var result = _sut.Validate(envelope);

        // Signature check runs first per CEC §2.1
        result.IsValid.Should().BeFalse();
        result.DetailCode.Should().Be("browser.artifactUnsigned");
    }

    [Fact]
    public void Validate_WrongRuntimeAndMixedEngine_ReportsRuntimeTypeErrorFirst()
    {
        var steps = new List<BrowserStep>
        {
            new() { StepId = "s1", Action = "browser.navigate" },
            new() { StepId = "s2", Action = "desktop.click" }
        };
        var envelope = ValidEnvelope(steps) with { RuntimeType = "shell" };

        var result = _sut.Validate(envelope);

        // runtimeType check runs before mixed-engine check
        result.IsValid.Should().BeFalse();
        result.DetailCode.Should().Be("browser.artifactTypeMismatch");
    }
}
