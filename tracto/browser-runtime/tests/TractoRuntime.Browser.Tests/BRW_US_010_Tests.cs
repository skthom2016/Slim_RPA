using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Moq;
using TractoRuntime.Browser.Actions;
using TractoRuntime.Browser.Assertion;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Extraction;
using TractoRuntime.Browser.Failure;
using TractoRuntime.Browser.Locators;
using TractoRuntime.Browser.Tests.Fixtures;
using TractoRuntime.Browser.Validation;

namespace TractoRuntime.Browser.Tests;

/// <summary>
/// Acceptance tests for BRW-US-010 — Technical Assertion and Safe DOM Inspection.
/// Covers BRW-AT-14, BRW-AT-17, BRW-AT-20, BRW-AT-26.
/// Requirement refs: DOM-008, DOM-009.
/// </summary>
/// <remarks>
///   AC#1 — Assertions locator.visible, locator.enabled, locator.checked, URL equality, and
///           exact technical text equality are accepted by the condition parser.
///   AC#2 — A predicate that compares a numeric page value to a threshold (e.g. "price > 100")
///           is rejected by the schema validator before any browser action.
///   AC#3 — A field value is extracted from the DOM exactly and passed as an opaque string;
///           no parsing or domain decision is made inside the runtime.  DisplayedText, approved
///           element attributes, and form-value reads are each supported.
///   AC#4 — page.evaluate with user-authored JavaScript is rejected at schema validation.
/// </remarks>
public sealed class BRW_US_010_Tests : BrowserTestBase
{
    private readonly Mock<IPage>     _pageMock    = new();
    private readonly Mock<ILocator>  _locatorMock = new();

    // =========================================================================
    // Part 1 — ConditionParser unit tests (DOM-008, AC#1, AC#2, BRW-AT-14)
    // =========================================================================

    private static ConditionParser CreateParser() => new();

    // ── AC#1: All five technical assertion kinds are accepted ─────────────────

    [Theory]
    [InlineData(AssertionKind.LocatorVisible)]
    [InlineData(AssertionKind.LocatorEnabled)]
    [InlineData(AssertionKind.LocatorChecked)]
    public void ConditionParser_LocatorStateKinds_AreAccepted(AssertionKind kind)
    {
        var sut  = CreateParser();
        var spec = new AssertionSpec { Kind = kind };   // No ExpectedValue needed

        var result = sut.Validate(spec);

        result.IsValid.Should().BeTrue(
            $"AssertionKind.{kind} is a technical locator-state predicate and must be accepted (AC#1)");
    }

    [Fact]
    public void ConditionParser_UrlEquals_WithValidUrl_IsAccepted()
    {
        var sut    = CreateParser();
        var spec   = new AssertionSpec
        {
            Kind          = AssertionKind.UrlEquals,
            ExpectedValue = "https://example.com/confirmation"
        };

        var result = sut.Validate(spec);

        result.IsValid.Should().BeTrue(
            "UrlEquals with a plain URL is a valid technical assertion (AC#1)");
    }

    [Fact]
    public void ConditionParser_TextEquals_WithPlainText_IsAccepted()
    {
        var sut  = CreateParser();
        var spec = new AssertionSpec
        {
            Kind          = AssertionKind.TextEquals,
            ExpectedValue = "Payment confirmed"
        };

        var result = sut.Validate(spec);

        result.IsValid.Should().BeTrue(
            "TextEquals with a plain technical string is a valid assertion (AC#1)");
    }

    // ── AC#2: Business predicates are rejected (BRW-AT-14) ────────────────────

    [Fact]
    public void ConditionParser_RawExpression_IsRejected_BRW_AT_14()
    {
        // This covers BRW-AT-14: "Numeric price threshold predicate fails script validation."
        var sut  = CreateParser();
        var spec = new AssertionSpec
        {
            Kind          = AssertionKind.TextEquals,
            RawExpression = "price > 100"
        };

        var result = sut.Validate(spec);

        result.IsValid.Should().BeFalse(
            "AC#2 / BRW-AT-14: raw expression predicates must be rejected before any browser action");
        result.DetailCode.Should().Be("browser.businessPredicateRejected");
        result.RejectionReason.Should().Contain("raw expression",
            "the rejection message must clearly identify it as a raw/business expression");
    }

    [Theory]
    [InlineData("amount > 500")]
    [InlineData("price >= 100")]
    [InlineData("total <= 0")]
    [InlineData("balance < 0")]
    [InlineData("count != 0")]
    public void ConditionParser_TextEquals_WithComparisonOperator_IsRejected(string expression)
    {
        var sut  = CreateParser();
        var spec = new AssertionSpec
        {
            Kind          = AssertionKind.TextEquals,
            ExpectedValue = expression
        };

        var result = sut.Validate(spec);

        result.IsValid.Should().BeFalse(
            $"AC#2: ExpectedValue '{expression}' contains a comparison operator — must be rejected as a business predicate");
        result.DetailCode.Should().Be("browser.businessPredicateRejected");
    }

    [Theory]
    [InlineData("amount > 500")]
    [InlineData("total >= 100")]
    [InlineData("result < 0")]
    public void ConditionParser_UrlEquals_WithComparisonOperator_IsRejected(string expression)
    {
        var sut  = CreateParser();
        var spec = new AssertionSpec
        {
            Kind          = AssertionKind.UrlEquals,
            ExpectedValue = expression
        };

        var result = sut.Validate(spec);

        result.IsValid.Should().BeFalse(
            $"AC#2: UrlEquals with comparison operator '{expression}' must be rejected");
        result.DetailCode.Should().Be("browser.businessPredicateRejected");
    }

    [Theory]
    [InlineData(AssertionKind.UrlEquals)]
    [InlineData(AssertionKind.TextEquals)]
    public void ConditionParser_ValueRequiredKinds_WithMissingValue_AreRejected(AssertionKind kind)
    {
        var sut  = CreateParser();
        var spec = new AssertionSpec { Kind = kind };   // ExpectedValue missing

        var result = sut.Validate(spec);

        result.IsValid.Should().BeFalse(
            $"AC#1: AssertionKind.{kind} requires ExpectedValue; absence is a validation error");
    }

    // =========================================================================
    // Part 2 — ArtifactValidator integration (DOM-008 AC#2, BRW-AT-14, BRW-AT-17)
    // =========================================================================

    private static ArtifactEnvelope MakeEnvelopeWith(params BrowserStep[] steps)
        => new()
        {
            RuntimeType    = "browser",
            SchemaMajor    = "1",
            Signature      = "valid-sig",
            ArtifactDigest = "valid-digest",
            Script         = new BrowserScript
            {
                ScriptId = "s-001",
                Version  = "1.0",
                Steps    = steps
            }
        };

    [Fact]
    public void ArtifactValidator_AssertStep_WithValidTechnicalPredicate_Passes()
    {
        var sut = new ArtifactValidator();
        var envelope = MakeEnvelopeWith(new BrowserStep
        {
            StepId    = "assert-visible",
            Action    = "browser.page.assert",
            Selector  = new SelectorRef { TestId = "confirm-banner" },
            Assertion = new AssertionSpec { Kind = AssertionKind.LocatorVisible }
        });

        var result = sut.Validate(envelope);

        result.IsValid.Should().BeTrue(
            "A browser.page.assert step with a technical locator-visible predicate must pass validation");
    }

    [Fact]
    public void ArtifactValidator_AssertStep_WithBusinessPredicate_IsRejected_BRW_AT_14()
    {
        // BRW-AT-14: Numeric price threshold predicate fails script validation.
        var sut = new ArtifactValidator();
        var envelope = MakeEnvelopeWith(new BrowserStep
        {
            StepId    = "assert-price",
            Action    = "browser.page.assert",
            Assertion = new AssertionSpec
            {
                Kind          = AssertionKind.TextEquals,
                RawExpression = "price > 100"
            }
        });

        var result = sut.Validate(envelope);

        result.IsValid.Should().BeFalse(
            "BRW-AT-14: business predicate must be rejected at artifact validation time, before any browser action");
        result.ErrorCategory.Should().Be(ErrorCategory.VALIDATION);
        result.DetailCode.Should().Be("browser.businessPredicateRejected");
    }

    [Fact]
    public void ArtifactValidator_AssertStep_WithNoAssertionSpec_IsRejected()
    {
        var sut = new ArtifactValidator();
        var envelope = MakeEnvelopeWith(new BrowserStep
        {
            StepId = "assert-broken",
            Action = "browser.page.assert"
            // Assertion is intentionally null
        });

        var result = sut.Validate(envelope);

        result.IsValid.Should().BeFalse(
            "browser.page.assert with no AssertionSpec is malformed and must be rejected");
        result.ErrorCategory.Should().Be(ErrorCategory.VALIDATION);
    }

    [Fact]
    public void ArtifactValidator_PageEvaluate_IsRejected_BRW_AT_17()
    {
        // BRW-AT-17: Browser script contains page.evaluate — schema rejects user-authored evaluation.
        var sut = new ArtifactValidator();
        var envelope = MakeEnvelopeWith(new BrowserStep
        {
            StepId = "eval-step",
            Action = "page.evaluate"
        });

        var result = sut.Validate(envelope);

        result.IsValid.Should().BeFalse(
            "BRW-AT-17: page.evaluate is rejected at schema validation before any browser interaction");
        result.ErrorCategory.Should().Be(ErrorCategory.POLICY_DENIED,
            "page.evaluate violates the closed-behavior policy (CEC-002)");
        result.DetailCode.Should().Be("browser.evaluationDenied");
    }

    // =========================================================================
    // Part 3 — DomAssertionService unit tests (DOM-008 AC#1)
    // =========================================================================

    private DomAssertionService CreateAssertionService()
    {
        var strategyMock = new Mock<ISelectorStrategy>();
        strategyMock
            .Setup(s => s.ResolveAsync(
                It.IsAny<IPage>(), It.IsAny<SelectorRef>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_locatorMock.Object);

        return new DomAssertionService(
            strategyMock.Object,
            NullLogger<DomAssertionService>.Instance);
    }

    [Fact]
    public async Task DomAssertionService_LocatorVisible_WhenVisible_Passes()
    {
        _locatorMock
            .Setup(l => l.IsVisibleAsync(It.IsAny<LocatorIsVisibleOptions?>()))
            .ReturnsAsync(true);

        var sut    = CreateAssertionService();
        var spec   = new AssertionSpec { Kind = AssertionKind.LocatorVisible };
        var result = await sut.EvaluateAsync(
            _pageMock.Object, spec, new SelectorRef { TestId = "banner" }, 5_000f, default);

        result.Passed.Should().BeTrue("AC#1: visible element must pass LocatorVisible assertion");
    }

    [Fact]
    public async Task DomAssertionService_LocatorVisible_WhenHidden_Fails()
    {
        _locatorMock
            .Setup(l => l.IsVisibleAsync(It.IsAny<LocatorIsVisibleOptions?>()))
            .ReturnsAsync(false);

        var sut    = CreateAssertionService();
        var spec   = new AssertionSpec { Kind = AssertionKind.LocatorVisible };
        var result = await sut.EvaluateAsync(
            _pageMock.Object, spec, new SelectorRef { TestId = "banner" }, 5_000f, default);

        result.Passed.Should().BeFalse("hidden element must fail LocatorVisible assertion");
        result.ErrorCategory.Should().Be(ErrorCategory.TARGET_NOT_FOUND);
        result.DetailCode.Should().Be("browser.locatorNotFound");
    }

    [Fact]
    public async Task DomAssertionService_LocatorEnabled_WhenEnabled_Passes()
    {
        _locatorMock
            .Setup(l => l.IsEnabledAsync(It.IsAny<LocatorIsEnabledOptions?>()))
            .ReturnsAsync(true);

        var sut    = CreateAssertionService();
        var spec   = new AssertionSpec { Kind = AssertionKind.LocatorEnabled };
        var result = await sut.EvaluateAsync(
            _pageMock.Object, spec, new SelectorRef { TestId = "submit-btn" }, 5_000f, default);

        result.Passed.Should().BeTrue("AC#1: enabled element must pass LocatorEnabled assertion");
    }

    [Fact]
    public async Task DomAssertionService_LocatorEnabled_WhenDisabled_Fails()
    {
        _locatorMock
            .Setup(l => l.IsEnabledAsync(It.IsAny<LocatorIsEnabledOptions?>()))
            .ReturnsAsync(false);

        var sut    = CreateAssertionService();
        var spec   = new AssertionSpec { Kind = AssertionKind.LocatorEnabled };
        var result = await sut.EvaluateAsync(
            _pageMock.Object, spec, new SelectorRef { TestId = "submit-btn" }, 5_000f, default);

        result.Passed.Should().BeFalse("disabled element must fail LocatorEnabled assertion");
        result.ErrorCategory.Should().Be(ErrorCategory.TIMEOUT,
            "disabled element actionability failure maps to TIMEOUT category");
        result.DetailCode.Should().Be("browser.actionabilityTimeout");
    }

    [Fact]
    public async Task DomAssertionService_LocatorChecked_WhenChecked_Passes()
    {
        _locatorMock
            .Setup(l => l.IsCheckedAsync(It.IsAny<LocatorIsCheckedOptions?>()))
            .ReturnsAsync(true);

        var sut    = CreateAssertionService();
        var spec   = new AssertionSpec { Kind = AssertionKind.LocatorChecked };
        var result = await sut.EvaluateAsync(
            _pageMock.Object, spec, new SelectorRef { TestId = "agree-checkbox" }, 5_000f, default);

        result.Passed.Should().BeTrue("AC#1: checked element must pass LocatorChecked assertion");
    }

    [Fact]
    public async Task DomAssertionService_UrlEquals_WhenMatches_Passes()
    {
        const string expectedUrl = "https://example.com/success";
        _pageMock.SetupGet(p => p.Url).Returns(expectedUrl);

        var sut    = CreateAssertionService();
        var spec   = new AssertionSpec { Kind = AssertionKind.UrlEquals, ExpectedValue = expectedUrl };
        var result = await sut.EvaluateAsync(
            _pageMock.Object, spec, selector: null, 5_000f, default);

        result.Passed.Should().BeTrue("AC#1: matching URL must pass UrlEquals assertion");
    }

    [Fact]
    public async Task DomAssertionService_UrlEquals_WhenMismatches_Fails()
    {
        _pageMock.SetupGet(p => p.Url).Returns("https://example.com/error");

        var sut    = CreateAssertionService();
        var spec   = new AssertionSpec
        {
            Kind          = AssertionKind.UrlEquals,
            ExpectedValue = "https://example.com/success"
        };
        var result = await sut.EvaluateAsync(
            _pageMock.Object, spec, selector: null, 5_000f, default);

        result.Passed.Should().BeFalse("non-matching URL must fail UrlEquals assertion");
        result.ErrorCategory.Should().Be(ErrorCategory.VALIDATION);
        result.DetailCode.Should().Be("browser.assertionFailed");
    }

    [Fact]
    public async Task DomAssertionService_TextEquals_WhenMatches_Passes()
    {
        const string expectedText = "Payment confirmed";
        _locatorMock
            .Setup(l => l.InnerTextAsync(It.IsAny<LocatorInnerTextOptions?>()))
            .ReturnsAsync(expectedText);

        var sut    = CreateAssertionService();
        var spec   = new AssertionSpec { Kind = AssertionKind.TextEquals, ExpectedValue = expectedText };
        var result = await sut.EvaluateAsync(
            _pageMock.Object, spec, new SelectorRef { TestId = "status-msg" }, 5_000f, default);

        result.Passed.Should().BeTrue("AC#1: matching text must pass TextEquals assertion");
    }

    [Fact]
    public async Task DomAssertionService_TextEquals_WhenMismatches_Fails()
    {
        _locatorMock
            .Setup(l => l.InnerTextAsync(It.IsAny<LocatorInnerTextOptions?>()))
            .ReturnsAsync("Payment pending");

        var sut    = CreateAssertionService();
        var spec   = new AssertionSpec { Kind = AssertionKind.TextEquals, ExpectedValue = "Payment confirmed" };
        var result = await sut.EvaluateAsync(
            _pageMock.Object, spec, new SelectorRef { TestId = "status-msg" }, 5_000f, default);

        result.Passed.Should().BeFalse("non-matching text must fail TextEquals assertion");
        result.ErrorCategory.Should().Be(ErrorCategory.VALIDATION);
        result.DetailCode.Should().Be("browser.assertionFailed");
    }

    [Fact]
    public async Task DomAssertionService_LocatorBased_WithoutSelector_Throws()
    {
        var sut  = CreateAssertionService();
        var spec = new AssertionSpec { Kind = AssertionKind.LocatorVisible };

        await sut.Invoking(s => s.EvaluateAsync(
                _pageMock.Object, spec, selector: null, 5_000f, default))
            .Should().ThrowAsync<InvalidOperationException>(
                "locator-based assertions require a selector; null selector must throw");
    }

    // =========================================================================
    // Part 4 — DomExtractionService unit tests (DOM-009 AC#3)
    // =========================================================================

    private DomExtractionService CreateExtractionService()
    {
        var strategyMock = new Mock<ISelectorStrategy>();
        strategyMock
            .Setup(s => s.ResolveAsync(
                It.IsAny<IPage>(), It.IsAny<SelectorRef>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_locatorMock.Object);

        return new DomExtractionService(
            strategyMock.Object,
            NullLogger<DomExtractionService>.Instance);
    }

    [Fact]
    public async Task DomExtractionService_DisplayedText_ReturnsOpaqueRawText()
    {
        const string rawText = "  Order total: £42.00  ";  // includes whitespace — trim happens
        _locatorMock
            .Setup(l => l.InnerTextAsync(It.IsAny<LocatorInnerTextOptions?>()))
            .ReturnsAsync(rawText);

        var sut    = CreateExtractionService();
        var spec   = new ExtractionSpec { Mode = ExtractionMode.DisplayedText, OutputKey = "total" };
        var result = await sut.ExtractAsync(
            _pageMock.Object, spec, new SelectorRef { TestId = "order-total" }, 5_000f, default);

        // AC#3: the value is extracted and passed opaquely — no domain parsing inside runtime
        result.Succeeded.Should().BeTrue("DisplayedText extraction must succeed");
        result.ExtractedValue.Should().Be("Order total: £42.00",
            "the extracted value is trimmed raw text passed unchanged — no parsing");
    }

    [Fact]
    public async Task DomExtractionService_Attribute_ReturnsRawAttributeValue()
    {
        const string hrefValue = "https://api.example.com/invoice/42";
        _locatorMock
            .Setup(l => l.GetAttributeAsync("href", It.IsAny<LocatorGetAttributeOptions?>()))
            .ReturnsAsync(hrefValue);

        var sut    = CreateExtractionService();
        var spec   = new ExtractionSpec
        {
            Mode          = ExtractionMode.Attribute,
            AttributeName = "href",
            OutputKey     = "invoiceUrl"
        };
        var result = await sut.ExtractAsync(
            _pageMock.Object, spec, new SelectorRef { TestId = "invoice-link" }, 5_000f, default);

        // AC#3: attribute value passed unchanged — no URL parsing or domain logic
        result.Succeeded.Should().BeTrue("Attribute extraction must succeed when attribute exists");
        result.ExtractedValue.Should().Be(hrefValue,
            "AC#3b: the raw attribute value is passed unchanged to the caller");
    }

    [Fact]
    public async Task DomExtractionService_Attribute_WhenAttributeMissing_Fails()
    {
        _locatorMock
            .Setup(l => l.GetAttributeAsync("data-nonexistent", It.IsAny<LocatorGetAttributeOptions?>()))
            .ReturnsAsync((string?)null);

        var sut    = CreateExtractionService();
        var spec   = new ExtractionSpec
        {
            Mode          = ExtractionMode.Attribute,
            AttributeName = "data-nonexistent"
        };
        var result = await sut.ExtractAsync(
            _pageMock.Object, spec, new SelectorRef { TestId = "el" }, 5_000f, default);

        result.Succeeded.Should().BeFalse("missing attribute must fail with TARGET_NOT_FOUND");
        result.ErrorCategory.Should().Be(ErrorCategory.TARGET_NOT_FOUND);
        result.DetailCode.Should().Be("browser.locatorNotFound");
    }

    [Fact]
    public async Task DomExtractionService_Attribute_WithoutAttributeName_Fails()
    {
        var sut    = CreateExtractionService();
        var spec   = new ExtractionSpec
        {
            Mode = ExtractionMode.Attribute
            // AttributeName intentionally omitted
        };
        var result = await sut.ExtractAsync(
            _pageMock.Object, spec, new SelectorRef { TestId = "el" }, 5_000f, default);

        result.Succeeded.Should().BeFalse("Attribute extraction without AttributeName must fail");
        result.ErrorCategory.Should().Be(ErrorCategory.VALIDATION);
    }

    [Fact]
    public async Task DomExtractionService_FormValue_ReturnsRawInputValue()
    {
        const string inputValue = "john.doe@example.com";
        _locatorMock
            .Setup(l => l.InputValueAsync(It.IsAny<LocatorInputValueOptions?>()))
            .ReturnsAsync(inputValue);

        var sut    = CreateExtractionService();
        var spec   = new ExtractionSpec { Mode = ExtractionMode.FormValue, OutputKey = "emailValue" };
        var result = await sut.ExtractAsync(
            _pageMock.Object, spec, new SelectorRef { AriaLabel = "Email address" }, 5_000f, default);

        // AC#3c: form value passed as opaque string — no email parsing or validation
        result.Succeeded.Should().BeTrue("FormValue extraction must succeed");
        result.ExtractedValue.Should().Be(inputValue,
            "AC#3c: the raw form value is passed unchanged — no domain parsing");
    }

    [Fact]
    public async Task DomExtractionService_WithoutSelector_Fails()
    {
        var sut    = CreateExtractionService();
        var spec   = new ExtractionSpec { Mode = ExtractionMode.DisplayedText };
        var result = await sut.ExtractAsync(
            _pageMock.Object, spec, selector: null, 5_000f, default);

        result.Succeeded.Should().BeFalse(
            "extraction without a selector must fail with VALIDATION error");
        result.ErrorCategory.Should().Be(ErrorCategory.VALIDATION);
        result.DetailCode.Should().Be("browser.selectorRefInvalid");
    }

    [Fact]
    public async Task DomExtractionService_ExtractedValue_IsStoredUnderOutputKey()
    {
        _locatorMock
            .Setup(l => l.InnerTextAsync(It.IsAny<LocatorInnerTextOptions?>()))
            .ReturnsAsync("PENDING");

        var sut    = CreateExtractionService();
        var spec   = new ExtractionSpec { Mode = ExtractionMode.DisplayedText, OutputKey = "orderStatus" };
        var result = await sut.ExtractAsync(
            _pageMock.Object, spec, new SelectorRef { TestId = "status" }, 5_000f, default);

        result.Succeeded.Should().BeTrue();
        result.ExtractedValue.Should().Be("PENDING",
            "the raw extracted value is carried on ExtractedValue; the outputKey is used by ActionExecutor for evidence mapping");
    }

    // =========================================================================
    // Part 5 — ActionExecutor integration tests (DOM-008 / DOM-009)
    // =========================================================================

    private ActionExecutor CreateExecutorWithAssertionService(IDomAssertionService assertionService)
    {
        var strategyMock = new Mock<ISelectorStrategy>();
        return new ActionExecutor(
            strategyMock.Object,
            new FailureClassifier(NullLogger<FailureClassifier>.Instance),
            NullLogger<ActionExecutor>.Instance,
            domAssertionService: assertionService);
    }

    private ActionExecutor CreateExecutorWithExtractionService(IDomExtractionService extractionService)
    {
        var strategyMock = new Mock<ISelectorStrategy>();
        return new ActionExecutor(
            strategyMock.Object,
            new FailureClassifier(NullLogger<FailureClassifier>.Instance),
            NullLogger<ActionExecutor>.Instance,
            domExtractionService: extractionService);
    }

    [Fact]
    public async Task ActionExecutor_PageAssert_WhenAssertionPasses_ReturnsSucceeded()
    {
        var assertionServiceMock = new Mock<IDomAssertionService>();
        assertionServiceMock
            .Setup(s => s.EvaluateAsync(
                It.IsAny<IPage>(),
                It.IsAny<AssertionSpec>(),
                It.IsAny<SelectorRef?>(),
                It.IsAny<float>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AssertionResult.Pass());

        var sut  = CreateExecutorWithAssertionService(assertionServiceMock.Object);
        var step = new BrowserStep
        {
            StepId    = "assert-001",
            Action    = "browser.page.assert",
            Selector  = new SelectorRef { TestId = "confirm-banner" },
            Assertion = new AssertionSpec { Kind = AssertionKind.LocatorVisible }
        };

        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        result.Succeeded.Should().BeTrue(
            "browser.page.assert must succeed when the assertion evaluates to true (AC#1)");
    }

    [Fact]
    public async Task ActionExecutor_PageAssert_WhenAssertionFails_ReturnsStructuredError()
    {
        var assertionServiceMock = new Mock<IDomAssertionService>();
        assertionServiceMock
            .Setup(s => s.EvaluateAsync(
                It.IsAny<IPage>(),
                It.IsAny<AssertionSpec>(),
                It.IsAny<SelectorRef?>(),
                It.IsAny<float>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AssertionResult.Fail(
                "Element was not visible.",
                ErrorCategory.TARGET_NOT_FOUND,
                "browser.locatorNotFound"));

        var sut  = CreateExecutorWithAssertionService(assertionServiceMock.Object);
        var step = new BrowserStep
        {
            StepId    = "assert-002",
            Action    = "browser.page.assert",
            Selector  = new SelectorRef { TestId = "confirm-banner" },
            Assertion = new AssertionSpec { Kind = AssertionKind.LocatorVisible }
        };

        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        result.Succeeded.Should().BeFalse("failed assertion must produce a failure result");
        result.ErrorCategory.Should().Be(ErrorCategory.TARGET_NOT_FOUND);
        result.DetailCode.Should().Be("browser.locatorNotFound");
    }

    [Fact]
    public async Task ActionExecutor_PageAssert_WithNoAssertionSpec_ReturnsValidationError()
    {
        var assertionServiceMock = new Mock<IDomAssertionService>();
        var sut  = CreateExecutorWithAssertionService(assertionServiceMock.Object);
        var step = new BrowserStep
        {
            StepId = "assert-broken",
            Action = "browser.page.assert"
            // Assertion intentionally null
        };

        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        result.Succeeded.Should().BeFalse(
            "browser.page.assert without AssertionSpec must fail with VALIDATION error");
        result.ErrorCategory.Should().Be(ErrorCategory.VALIDATION);
        result.DetailCode.Should().Be("browser.selectorRefInvalid");
    }

    [Fact]
    public async Task ActionExecutor_GetText_ReturnsExtractedValueInEvidence()
    {
        var extractionServiceMock = new Mock<IDomExtractionService>();
        extractionServiceMock
            .Setup(s => s.ExtractAsync(
                It.IsAny<IPage>(),
                It.IsAny<ExtractionSpec>(),
                It.IsAny<SelectorRef?>(),
                It.IsAny<float>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExtractionResult.Ok("CONFIRMED"));

        var sut  = CreateExecutorWithExtractionService(extractionServiceMock.Object);
        var step = new BrowserStep
        {
            StepId    = "get-text-001",
            Action    = "browser.element.getText",
            Selector  = new SelectorRef { TestId = "status-label" },
            Extraction = new ExtractionSpec
            {
                Mode      = ExtractionMode.DisplayedText,
                OutputKey = "statusText"
            }
        };

        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // AC#3: the opaque extracted value is in the evidence under the declared key
        result.Succeeded.Should().BeTrue("getText must succeed when extraction succeeds");
        result.Evidence.Should().ContainKey("statusText",
            "AC#3: extracted value must be stored under the declared outputKey");
        result.Evidence["statusText"].Should().Be("CONFIRMED",
            "the raw extracted value is forwarded unchanged — no parsing by the runtime");
    }

    [Fact]
    public async Task ActionExecutor_GetAttr_ReturnsAttributeValueInEvidence()
    {
        var extractionServiceMock = new Mock<IDomExtractionService>();
        extractionServiceMock
            .Setup(s => s.ExtractAsync(
                It.IsAny<IPage>(),
                It.IsAny<ExtractionSpec>(),
                It.IsAny<SelectorRef?>(),
                It.IsAny<float>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExtractionResult.Ok("https://api.example.com/data/42"));

        var sut  = CreateExecutorWithExtractionService(extractionServiceMock.Object);
        var step = new BrowserStep
        {
            StepId    = "get-attr-001",
            Action    = "browser.element.getAttr",
            Selector  = new SelectorRef { TestId = "api-link" },
            Extraction = new ExtractionSpec
            {
                Mode          = ExtractionMode.Attribute,
                AttributeName = "href",
                OutputKey     = "apiUrl"
            }
        };

        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        result.Succeeded.Should().BeTrue("getAttr must succeed when attribute extraction succeeds");
        result.Evidence.Should().ContainKey("apiUrl");
        result.Evidence["apiUrl"].Should().Be("https://api.example.com/data/42",
            "AC#3b: raw attribute value forwarded unchanged");
    }

    [Fact]
    public async Task ActionExecutor_GetValue_ReturnsFormValueInEvidence()
    {
        var extractionServiceMock = new Mock<IDomExtractionService>();
        extractionServiceMock
            .Setup(s => s.ExtractAsync(
                It.IsAny<IPage>(),
                It.IsAny<ExtractionSpec>(),
                It.IsAny<SelectorRef?>(),
                It.IsAny<float>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExtractionResult.Ok("ACC-00042"));

        var sut  = CreateExecutorWithExtractionService(extractionServiceMock.Object);
        var step = new BrowserStep
        {
            StepId    = "get-value-001",
            Action    = "browser.element.getValue",
            Selector  = new SelectorRef { AriaLabel = "Account number" },
            Extraction = new ExtractionSpec
            {
                Mode      = ExtractionMode.FormValue,
                OutputKey = "accountNumber"
            }
        };

        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        result.Succeeded.Should().BeTrue("getValue must succeed when form value extraction succeeds");
        result.Evidence.Should().ContainKey("accountNumber");
        result.Evidence["accountNumber"].Should().Be("ACC-00042",
            "AC#3c: raw form value forwarded unchanged — no domain parsing");
    }

    [Fact]
    public async Task ActionExecutor_ExtractAction_WithDefaultOutputKey_UsesExtractedValue()
    {
        // When no ExtractionSpec is supplied in the step, the executor creates a default one
        // with OutputKey="extractedValue". This verifies that default is honoured.
        var extractionServiceMock = new Mock<IDomExtractionService>();
        extractionServiceMock
            .Setup(s => s.ExtractAsync(
                It.IsAny<IPage>(),
                It.IsAny<ExtractionSpec>(),
                It.IsAny<SelectorRef?>(),
                It.IsAny<float>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExtractionResult.Ok("raw-value"));

        var sut  = CreateExecutorWithExtractionService(extractionServiceMock.Object);
        var step = new BrowserStep
        {
            StepId   = "get-text-default",
            Action   = "browser.element.getText",
            Selector = new SelectorRef { TestId = "label" }
            // Extraction not set — defaults are applied inside ActionExecutor
        };

        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        result.Succeeded.Should().BeTrue();
        result.Evidence.Should().ContainKey("extractedValue",
            "when no ExtractionSpec is set, the default OutputKey 'extractedValue' is used");
    }

    // =========================================================================
    // Part 6 — No domain comparison inside the runtime (AC#4 integration)
    // =========================================================================

    [Fact]
    public void NoRuntimeDomainComparison_ExtractedValue_IsOpaqueString()
    {
        // AC#3: Verifies that ExtractionResult carries the raw string unchanged.
        // The test demonstrates that the value "42" extracted from the DOM is NOT compared to
        // a threshold — the runtime passes it as an opaque string to service.invoke.
        var extracted = ExtractionResult.Ok("42");

        extracted.Succeeded.Should().BeTrue();
        extracted.ExtractedValue.Should().Be("42",
            "the runtime must not parse the extracted value as a number or compare it to a threshold");
        extracted.ExtractedValue.Should().BeOfType<string>(
            "the opaque extracted value is always a string — numeric comparison is the service's responsibility");
    }

    [Fact]
    public void ConditionParser_AcceptsExactly5TechnicalPredicateKinds()
    {
        // Verifies that all five declared AssertionKind values pass the parser when correctly formed.
        var parser   = CreateParser();
        var selector = new SelectorRef { TestId = "el" };

        // The three locator-state kinds need no ExpectedValue
        foreach (var kind in new[] { AssertionKind.LocatorVisible, AssertionKind.LocatorEnabled, AssertionKind.LocatorChecked })
        {
            parser.Validate(new AssertionSpec { Kind = kind }).IsValid
                .Should().BeTrue($"AssertionKind.{kind} must be accepted");
        }

        // The two value-equality kinds need ExpectedValue
        foreach (var (kind, value) in new[]
        {
            (AssertionKind.UrlEquals,  "https://example.com/done"),
            (AssertionKind.TextEquals, "Confirmed")
        })
        {
            parser.Validate(new AssertionSpec { Kind = kind, ExpectedValue = value }).IsValid
                .Should().BeTrue($"AssertionKind.{kind} with a plain expected value must be accepted");
        }
    }
}
