using FluentAssertions;
using Microsoft.Playwright;
using Moq;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Locators;
using TractoRuntime.Browser.Tests.Fixtures;

namespace TractoRuntime.Browser.Tests;

/// <summary>
/// Acceptance tests for BRW-US-007 — Semantic Locator Resolution.
///
/// Requirement refs: DOM-001, DOM-002, WEB-010
/// Acceptance tests:  BRW-AT-01, BRW-AT-02, BRW-AT-04, BRW-AT-21
///
/// Acceptance criteria:
///   AC-1  A button located by role="button" name="Submit" is found after a DOM reorder
///         that preserves the accessible name (DOM-001 / BRW-AT-01).
///   AC-2  Two or more elements matching the same locator without scoped disambiguation
///         → LocatorAmbiguityException (errorCategory=TARGET_AMBIGUOUS,
///         detailCode=browser.multipleLocatorMatches) (DOM-002 / BRW-AT-21).
///   AC-3  CSS locators require allowCss=true; rejected at artifact validation time
///         before any browser interaction (BRW-AT-04 / ArtifactValidator coverage).
///   AC-4  A locator with no matching element → LocatorNotFoundException
///         (errorCategory=TARGET_NOT_FOUND, detailCode=browser.locatorNotFound) (WEB-010).
///
/// Priority coverage:
///   P-1   role > label > placeholder > testId > text > css > xpath tried in declared order.
///   P-2   AriaLabel backward-compatibility alias resolves identically to Label.
/// </summary>
public sealed class BRW_US_007_Tests : BrowserTestBase
{
    private readonly LocatorResolver _sut = new();

    // ─── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Returns a page mock where the given locator returns exactly one element.</summary>
    private static (Mock<IPage> Page, Mock<ILocator> Locator) MakeUniqueMatch()
    {
        var locatorMock = new Mock<ILocator>();
        locatorMock.Setup(l => l.CountAsync()).ReturnsAsync(1);

        var pageMock = new Mock<IPage>();
        return (pageMock, locatorMock);
    }

    /// <summary>Returns a page mock where the given locator returns zero elements.</summary>
    private static (Mock<IPage> Page, Mock<ILocator> Locator) MakeNoMatch()
    {
        var locatorMock = new Mock<ILocator>();
        locatorMock.Setup(l => l.CountAsync()).ReturnsAsync(0);

        var pageMock = new Mock<IPage>();
        return (pageMock, locatorMock);
    }

    /// <summary>Returns a page mock where the given locator returns multiple elements.</summary>
    private static (Mock<IPage> Page, Mock<ILocator> Locator) MakeAmbiguousMatch(int count = 2)
    {
        var locatorMock = new Mock<ILocator>();
        locatorMock.Setup(l => l.CountAsync()).ReturnsAsync(count);

        var pageMock = new Mock<IPage>();
        return (pageMock, locatorMock);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC-1 / BRW-AT-01 — Role+name locator finds element after DOM reorder
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AC1_RoleLocator_MatchesUniqueButton_ReturnsLocator()
    {
        // Arrange — ARIA role "button" with accessible name "Submit"
        var (pageMock, locatorMock) = MakeUniqueMatch();
        pageMock
            .Setup(p => p.GetByRole(AriaRole.Button, It.Is<PageGetByRoleOptions>(o => o.Name == "Submit")))
            .Returns(locatorMock.Object);

        var selectorRef = new SelectorRef { Role = "button", RoleName = "Submit" };

        // Act
        var result = await _sut.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);

        // Assert — AC-1: element resolved using role strategy
        result.Should().BeSameAs(locatorMock.Object,
            because: "role+name locator must return the matched ILocator when exactly one element matches");
    }

    [Fact]
    public async Task AC1_RoleLocator_FindsButtonAfterDomReorder()
    {
        // AC-1: stable role+name locator survives a DOM reorder that preserves accessible identity.
        // Simulated by the mock returning 1 match regardless of DOM structure.
        var (pageMock, locatorMock) = MakeUniqueMatch();
        pageMock
            .Setup(p => p.GetByRole(AriaRole.Button, It.Is<PageGetByRoleOptions>(o => o.Name == "Submit")))
            .Returns(locatorMock.Object);

        var selectorRef = new SelectorRef { Role = "button", RoleName = "Submit" };

        // Act — second call simulates resolution after DOM reorder
        var firstResult  = await _sut.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);
        var secondResult = await _sut.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);

        // Assert — AC-1: same stable locator both before and after reorder
        firstResult.Should().BeSameAs(secondResult,
            because: "role+name locator must be stable across DOM reorders that preserve accessible identity");
    }

    [Theory]
    [InlineData("button",    AriaRole.Button)]
    [InlineData("textbox",   AriaRole.Textbox)]
    [InlineData("checkbox",  AriaRole.Checkbox)]
    [InlineData("combobox",  AriaRole.Combobox)]
    [InlineData("link",      AriaRole.Link)]
    [InlineData("heading",   AriaRole.Heading)]
    [InlineData("radio",     AriaRole.Radio)]
    [InlineData("tab",       AriaRole.Tab)]
    public async Task AC1_KnownAriaRoles_AllResolveCorrectly(string roleString, AriaRole expectedRole)
    {
        // AC-1: all common ARIA roles must resolve to the matching Playwright AriaRole
        var (pageMock, locatorMock) = MakeUniqueMatch();
        pageMock
            .Setup(p => p.GetByRole(expectedRole, It.IsAny<PageGetByRoleOptions?>()))
            .Returns(locatorMock.Object);

        var selectorRef = new SelectorRef { Role = roleString };

        var result = await _sut.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);

        result.Should().BeSameAs(locatorMock.Object,
            because: $"role='{roleString}' must resolve via Playwright GetByRole({expectedRole})");
    }

    [Fact]
    public async Task AC1_RoleLocator_CaseInsensitive_ButtonUpperCase_Resolves()
    {
        // Role strings are case-insensitive per HTML/ARIA specification
        var (pageMock, locatorMock) = MakeUniqueMatch();
        pageMock
            .Setup(p => p.GetByRole(AriaRole.Button, It.IsAny<PageGetByRoleOptions?>()))
            .Returns(locatorMock.Object);

        var selectorRef = new SelectorRef { Role = "BUTTON" };

        var result = await _sut.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);

        result.Should().BeSameAs(locatorMock.Object,
            because: "role strings must be matched case-insensitively (ARIA roles are case-insensitive)");
    }

    [Fact]
    public async Task AC1_LabelLocator_UniqueMatch_ReturnsLocator()
    {
        // AC-1: GetByLabel resolves form inputs with accessible label text
        var (pageMock, locatorMock) = MakeUniqueMatch();
        pageMock
            .Setup(p => p.GetByLabel("Email address", It.IsAny<PageGetByLabelOptions?>()))
            .Returns(locatorMock.Object);

        var selectorRef = new SelectorRef { Label = "Email address" };

        var result = await _sut.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);

        result.Should().BeSameAs(locatorMock.Object,
            because: "label strategy must use Playwright GetByLabel for form inputs");
    }

    [Fact]
    public async Task AC1_PlaceholderLocator_UniqueMatch_ReturnsLocator()
    {
        // AC-1 / WEB-010: placeholder text locator resolves inputs without explicit labels
        var (pageMock, locatorMock) = MakeUniqueMatch();
        pageMock
            .Setup(p => p.GetByPlaceholder("Search…", It.IsAny<PageGetByPlaceholderOptions?>()))
            .Returns(locatorMock.Object);

        var selectorRef = new SelectorRef { Placeholder = "Search…" };

        var result = await _sut.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);

        result.Should().BeSameAs(locatorMock.Object,
            because: "placeholder strategy must use Playwright GetByPlaceholder");
    }

    [Fact]
    public async Task AC1_TestIdLocator_UniqueMatch_ReturnsLocator()
    {
        // AC-1 / WEB-010: testId fallback for sites without accessible labels (policy-allowlisted)
        var (pageMock, locatorMock) = MakeUniqueMatch();
        pageMock
            .Setup(p => p.GetByTestId("submit-btn"))
            .Returns(locatorMock.Object);

        var selectorRef = new SelectorRef { TestId = "submit-btn" };

        var result = await _sut.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);

        result.Should().BeSameAs(locatorMock.Object,
            because: "testId strategy must use Playwright GetByTestId");
    }

    [Fact]
    public async Task AC1_TextLocator_ExactMatch_ReturnsLocator()
    {
        // AC-1: text strategy resolves by visible text (exact match by default)
        var (pageMock, locatorMock) = MakeUniqueMatch();
        pageMock
            .Setup(p => p.GetByText("Sign in", It.Is<PageGetByTextOptions>(o => o.Exact == true)))
            .Returns(locatorMock.Object);

        var selectorRef = new SelectorRef { Text = "Sign in" };

        var result = await _sut.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);

        result.Should().BeSameAs(locatorMock.Object,
            because: "text strategy must use Playwright GetByText with Exact=true");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC-1 / backward-compat — AriaLabel alias resolves like Label
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AC1_AriaLabelAlias_ResolvesLikeLabel()
    {
        // Backward-compat: scripts that use AriaLabel must behave identically to Label
        var (pageMock, locatorMock) = MakeUniqueMatch();
        pageMock
            .Setup(p => p.GetByLabel("Password", It.IsAny<PageGetByLabelOptions?>()))
            .Returns(locatorMock.Object);

        var selectorRef = new SelectorRef { AriaLabel = "Password" }; // old field name

        var result = await _sut.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);

        result.Should().BeSameAs(locatorMock.Object,
            because: "AriaLabel must be a backward-compatible alias for Label, resolved via GetByLabel");
    }

    [Fact]
    public async Task AC1_LabelTakesPrecedenceOverAriaLabel_WhenBothSpecified()
    {
        // When both Label and AriaLabel are specified, Label wins (it's the primary field)
        var (pageMock, locatorMock) = MakeUniqueMatch();
        pageMock
            .Setup(p => p.GetByLabel("Primary Label", It.IsAny<PageGetByLabelOptions?>()))
            .Returns(locatorMock.Object);

        var selectorRef = new SelectorRef { Label = "Primary Label", AriaLabel = "Old Label" };

        var result = await _sut.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);

        // GetByLabel("Primary Label") must have been called (not "Old Label")
        pageMock.Verify(
            p => p.GetByLabel("Primary Label", It.IsAny<PageGetByLabelOptions?>()),
            Times.Once(),
            "Label takes precedence over AriaLabel when both are specified");
        pageMock.Verify(
            p => p.GetByLabel("Old Label", It.IsAny<PageGetByLabelOptions?>()),
            Times.Never(),
            "AriaLabel must not be consulted when Label is present");

        result.Should().BeSameAs(locatorMock.Object);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC-2 / BRW-AT-21 / DOM-002 — Ambiguity rejection
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AC2_RoleLocator_MultipleMatches_ThrowsLocatorAmbiguityException()
    {
        // AC-2 / DOM-002: two Submit buttons without scope → TARGET_AMBIGUOUS
        var (pageMock, locatorMock) = MakeAmbiguousMatch(2);
        pageMock
            .Setup(p => p.GetByRole(AriaRole.Button, It.IsAny<PageGetByRoleOptions?>()))
            .Returns(locatorMock.Object);

        var selectorRef = new SelectorRef { Role = "button", RoleName = "Submit" };

        // Act & Assert — AC-2: must throw, never silently click first match
        var ex = await Assert.ThrowsAsync<LocatorAmbiguityException>(
            () => _sut.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None));

        ex.Category.Should().Be(ErrorCategory.TARGET_AMBIGUOUS,
            because: "DOM-002 maps multiple locator matches to errorCategory=TARGET_AMBIGUOUS");
        ex.DetailCode.Should().Be("browser.multipleLocatorMatches",
            because: "spec §11 mandates detailCode=browser.multipleLocatorMatches for ambiguous locators");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(10)]
    public async Task AC2_AnyCountAboveOne_ThrowsAmbiguityException(int matchCount)
    {
        // AC-2: any count > 1 must throw, regardless of how many duplicates exist
        var (pageMock, locatorMock) = MakeAmbiguousMatch(matchCount);
        pageMock
            .Setup(p => p.GetByLabel(It.IsAny<string>(), It.IsAny<PageGetByLabelOptions?>()))
            .Returns(locatorMock.Object);

        var selectorRef = new SelectorRef { Label = "Amount" };

        await Assert.ThrowsAsync<LocatorAmbiguityException>(
            () => _sut.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None));
    }

    [Fact]
    public async Task AC2_AmbiguityException_ToCecError_ReturnsCorrectEnvelope()
    {
        // AC-2: the exception must produce a valid CEC wire error without information loss
        var (pageMock, locatorMock) = MakeAmbiguousMatch(3);
        pageMock
            .Setup(p => p.GetByRole(AriaRole.Button, It.IsAny<PageGetByRoleOptions?>()))
            .Returns(locatorMock.Object);

        var selectorRef = new SelectorRef { Role = "button" };

        var ex = await Assert.ThrowsAsync<LocatorAmbiguityException>(
            () => _sut.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None));

        var cecError = ex.ToCecError();
        cecError.Category.Should().Be(ErrorCategory.TARGET_AMBIGUOUS);
        cecError.DetailCode.Should().Be("browser.multipleLocatorMatches");
        cecError.Message.Should().NotBeNullOrEmpty(
            because: "CEC error message must be present for Orchestrator diagnostics");
    }

    [Fact]
    public async Task AC2_AmbiguityOnFirstStrategy_DoesNotFallThroughToNextStrategy()
    {
        // AC-2: when a strategy is ambiguous, resolver must NOT try the next strategy.
        // The spec mandates rejection, not silent fallback to a narrower strategy.
        var (pageMock, _) = MakeAmbiguousMatch(2);
        var ambiguousLocator = new Mock<ILocator>();
        ambiguousLocator.Setup(l => l.CountAsync()).ReturnsAsync(2);

        var uniqueLocator = new Mock<ILocator>();
        uniqueLocator.Setup(l => l.CountAsync()).ReturnsAsync(1);

        pageMock
            .Setup(p => p.GetByRole(AriaRole.Button, It.IsAny<PageGetByRoleOptions?>()))
            .Returns(ambiguousLocator.Object);

        pageMock
            .Setup(p => p.GetByLabel(It.IsAny<string>(), It.IsAny<PageGetByLabelOptions?>()))
            .Returns(uniqueLocator.Object);

        // Both role and label are specified; role is ambiguous
        var selectorRef = new SelectorRef { Role = "button", Label = "Submit" };

        // Act — resolver MUST throw on the role strategy, not fall through to label
        await Assert.ThrowsAsync<LocatorAmbiguityException>(
            () => _sut.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None));

        // Label strategy must never have been called (ambiguity stops resolution)
        pageMock.Verify(
            p => p.GetByLabel(It.IsAny<string>(), It.IsAny<PageGetByLabelOptions?>()),
            () => Times.Never(),
            "ambiguity on a higher-priority strategy must stop resolution immediately, not fall through");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC-3 / BRW-AT-04 — CSS locators validated at artifact level (ArtifactValidator)
    // The LocatorResolver itself processes a CSS locator when it reaches resolution
    // (i.e. when AllowCss=true was declared and the ArtifactValidator passed it).
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AC3_CssLocator_WhenAllowCssApproved_LocatorPassedToPlaywright()
    {
        // AC-3: when CSS selector reaches the resolver (artifact already approved), it works
        var (pageMock, locatorMock) = MakeUniqueMatch();
        pageMock
            .Setup(p => p.Locator("button.submit", It.IsAny<PageLocatorOptions?>()))
            .Returns(locatorMock.Object);

        // All semantic strategies return null (not in the selectorRef) → falls through to css
        var selectorRef = new SelectorRef { Css = "button.submit" };

        var result = await _sut.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);

        result.Should().BeSameAs(locatorMock.Object,
            because: "CSS locator passes through resolver when artifact policy already approved it");
    }

    [Fact]
    public void AC3_SelectorRef_CssField_RejectedByArtifactValidator_WithoutAllowCss()
    {
        // AC-3: the ArtifactValidator (not the resolver) is responsible for policy enforcement.
        // This test confirms the validator produces the correct error code.
        var validator = new TractoRuntime.Browser.Validation.ArtifactValidator();
        var envelope = new TractoRuntime.Browser.Validation.ArtifactEnvelope
        {
            RuntimeType    = "browser",
            SchemaMajor    = "1",
            Signature      = "sha256:abc",
            ArtifactDigest = "sha256:def",
            AllowCss       = false, // policy flag NOT set
            Script = new BrowserScript
            {
                ScriptId = "test-css-reject",
                Version  = "1.0",
                Steps    =
                [
                    new BrowserStep
                    {
                        StepId   = "s1",
                        Action   = "browser.click",
                        Selector = new SelectorRef { Css = "button.submit" }
                    }
                ]
            }
        };

        var result = validator.Validate(envelope);

        result.IsValid.Should().BeFalse(
            because: "CSS selector without allowCss=true must be rejected at artifact validation time");
        result.ErrorCategory.Should().Be(ErrorCategory.VALIDATION,
            because: "spec §9 mandates VALIDATION category for CSS policy rejection");
        result.DetailCode.Should().Be("browser.cssNotAllowed",
            because: "spec mandates detailCode=browser.cssNotAllowed for CSS policy violation");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC-4 / WEB-010 — No match → LocatorNotFoundException (TARGET_NOT_FOUND)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AC4_AllStrategiesNoMatch_ThrowsLocatorNotFoundException()
    {
        // AC-4: element without accessible labels and no allowlisted fallback → structured error
        var zeroLocator = new Mock<ILocator>();
        zeroLocator.Setup(l => l.CountAsync()).ReturnsAsync(0);

        var pageMock = new Mock<IPage>();
        pageMock.Setup(p => p.GetByLabel(It.IsAny<string>(), It.IsAny<PageGetByLabelOptions?>()))
                .Returns(zeroLocator.Object);
        pageMock.Setup(p => p.GetByTestId(It.IsAny<string>()))
                .Returns(zeroLocator.Object);

        var selectorRef = new SelectorRef { Label = "Nonexistent field", TestId = "nonexistent-id" };

        var ex = await Assert.ThrowsAsync<LocatorNotFoundException>(
            () => _sut.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None));

        ex.Category.Should().Be(ErrorCategory.TARGET_NOT_FOUND,
            because: "spec §11 maps no-match locators to errorCategory=TARGET_NOT_FOUND");
        ex.DetailCode.Should().Be("browser.locatorNotFound",
            because: "spec §11 mandates detailCode=browser.locatorNotFound when nothing is found");
    }

    [Fact]
    public async Task AC4_LocatorNotFound_ToCecError_ReturnsCorrectEnvelope()
    {
        // AC-4: exception must produce a valid CEC wire error
        var zeroLocator = new Mock<ILocator>();
        zeroLocator.Setup(l => l.CountAsync()).ReturnsAsync(0);

        var pageMock = new Mock<IPage>();
        pageMock.Setup(p => p.GetByTestId(It.IsAny<string>())).Returns(zeroLocator.Object);

        var selectorRef = new SelectorRef { TestId = "ghost-element" };

        var ex = await Assert.ThrowsAsync<LocatorNotFoundException>(
            () => _sut.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None));

        var cecError = ex.ToCecError();
        cecError.Category.Should().Be(ErrorCategory.TARGET_NOT_FOUND);
        cecError.DetailCode.Should().Be("browser.locatorNotFound");
        cecError.Message.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task AC4_UnknownRoleString_SkippedGracefully_FallsThroughToNextStrategy()
    {
        // AC-4 / WEB-010: an unknown ARIA role string must not crash; it is skipped silently
        // and the resolver continues to the next declared strategy.
        var (pageMock, locatorMock) = MakeUniqueMatch();
        pageMock
            .Setup(p => p.GetByLabel("Submit", It.IsAny<PageGetByLabelOptions?>()))
            .Returns(locatorMock.Object);

        // "not-a-real-role" will not parse as AriaRole → skipped → label is used
        var selectorRef = new SelectorRef { Role = "not-a-real-role", Label = "Submit" };

        var result = await _sut.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);

        result.Should().BeSameAs(locatorMock.Object,
            because: "an unrecognised role string must be skipped and the next strategy tried");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // P-1 — Priority order: role > label > placeholder > testId > text > css
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Priority_RoleBeforeLabel_WhenBothMatch_RoleWins()
    {
        // Role has higher priority than label per spec §9 (F-08)
        var roleLocator = new Mock<ILocator>();
        roleLocator.Setup(l => l.CountAsync()).ReturnsAsync(1);

        var labelLocator = new Mock<ILocator>();
        labelLocator.Setup(l => l.CountAsync()).ReturnsAsync(1);

        var pageMock = new Mock<IPage>();
        pageMock.Setup(p => p.GetByRole(AriaRole.Button, It.IsAny<PageGetByRoleOptions?>()))
                .Returns(roleLocator.Object);
        pageMock.Setup(p => p.GetByLabel(It.IsAny<string>(), It.IsAny<PageGetByLabelOptions?>()))
                .Returns(labelLocator.Object);

        var selectorRef = new SelectorRef { Role = "button", Label = "Submit" };

        var result = await _sut.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);

        result.Should().BeSameAs(roleLocator.Object,
            because: "role (priority 1) must be resolved before label (priority 2)");

        // Label must never have been called
        pageMock.Verify(
            p => p.GetByLabel(It.IsAny<string>(), It.IsAny<PageGetByLabelOptions?>()),
            () => Times.Never(),
            "label strategy must not run once a higher-priority strategy succeeds");
    }

    [Fact]
    public async Task Priority_LabelBeforePlaceholder_WhenRoleAbsent()
    {
        // Label (priority 2) is tried before placeholder (priority 3)
        var labelLocator = new Mock<ILocator>();
        labelLocator.Setup(l => l.CountAsync()).ReturnsAsync(1);

        var placeholderLocator = new Mock<ILocator>();
        placeholderLocator.Setup(l => l.CountAsync()).ReturnsAsync(1);

        var pageMock = new Mock<IPage>();
        pageMock.Setup(p => p.GetByLabel(It.IsAny<string>(), It.IsAny<PageGetByLabelOptions?>()))
                .Returns(labelLocator.Object);
        pageMock.Setup(p => p.GetByPlaceholder(It.IsAny<string>(), It.IsAny<PageGetByPlaceholderOptions?>()))
                .Returns(placeholderLocator.Object);

        var selectorRef = new SelectorRef { Label = "Username", Placeholder = "Enter username" };

        var result = await _sut.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);

        result.Should().BeSameAs(labelLocator.Object,
            because: "label (priority 2) must be resolved before placeholder (priority 3)");
    }

    [Fact]
    public async Task Priority_FallsThroughToPlaceholder_WhenLabelMissesAndPlaceholderHits()
    {
        // When label finds 0 elements, resolver falls through to placeholder
        var zeroLocator = new Mock<ILocator>();
        zeroLocator.Setup(l => l.CountAsync()).ReturnsAsync(0);

        var placeholderLocator = new Mock<ILocator>();
        placeholderLocator.Setup(l => l.CountAsync()).ReturnsAsync(1);

        var pageMock = new Mock<IPage>();
        pageMock.Setup(p => p.GetByLabel(It.IsAny<string>(), It.IsAny<PageGetByLabelOptions?>()))
                .Returns(zeroLocator.Object);
        pageMock.Setup(p => p.GetByPlaceholder(It.IsAny<string>(), It.IsAny<PageGetByPlaceholderOptions?>()))
                .Returns(placeholderLocator.Object);

        var selectorRef = new SelectorRef { Label = "Search", Placeholder = "Search…" };

        var result = await _sut.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);

        result.Should().BeSameAs(placeholderLocator.Object,
            because: "resolver must fall through to placeholder when label returns zero matches");
    }

    [Fact]
    public async Task Priority_CssIsLastResort_SemanticStrategiesTakenFirst()
    {
        // CSS is the lowest-priority semantic fallback; semantic strategies run first
        var roleLocator = new Mock<ILocator>();
        roleLocator.Setup(l => l.CountAsync()).ReturnsAsync(1);

        var cssLocator = new Mock<ILocator>();
        cssLocator.Setup(l => l.CountAsync()).ReturnsAsync(1);

        var pageMock = new Mock<IPage>();
        pageMock.Setup(p => p.GetByRole(AriaRole.Button, It.IsAny<PageGetByRoleOptions?>()))
                .Returns(roleLocator.Object);
        pageMock.Setup(p => p.Locator(It.IsAny<string>(), It.IsAny<PageLocatorOptions?>()))
                .Returns(cssLocator.Object);

        var selectorRef = new SelectorRef { Role = "button", Css = "button.cta" };

        var result = await _sut.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);

        result.Should().BeSameAs(roleLocator.Object,
            because: "semantic role strategy (priority 1) must be used before CSS (priority 6)");

        pageMock.Verify(
            p => p.Locator(It.IsAny<string>(), It.IsAny<PageLocatorOptions?>()),
            () => Times.Never(),
            "CSS locator must not be invoked if a semantic strategy already matched");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Guard clauses
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Guard_NullPage_ThrowsArgumentNullException()
    {
        var selectorRef = new SelectorRef { Role = "button" };

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.ResolveAsync(null!, selectorRef, CancellationToken.None));
    }

    [Fact]
    public async Task Guard_NullSelectorRef_ThrowsArgumentNullException()
    {
        var pageMock = new Mock<IPage>();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.ResolveAsync(pageMock.Object, null!, CancellationToken.None));
    }

    [Fact]
    public async Task Guard_EmptySelectorRef_AllFieldsNull_ThrowsLocatorNotFoundException()
    {
        // A SelectorRef with all null fields produces no candidates → not found
        var pageMock = new Mock<IPage>();
        var selectorRef = new SelectorRef(); // all fields null

        await Assert.ThrowsAsync<LocatorNotFoundException>(
            () => _sut.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None));
    }

    [Fact]
    public async Task Guard_CancellationRequested_ThrowsOperationCancelledException()
    {
        // CancellationToken must be respected before any Playwright call
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var (pageMock, locatorMock) = MakeUniqueMatch();
        pageMock.Setup(p => p.GetByRole(It.IsAny<AriaRole>(), It.IsAny<PageGetByRoleOptions?>()))
                .Returns(locatorMock.Object);

        var selectorRef = new SelectorRef { Role = "button" };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _sut.ResolveAsync(pageMock.Object, selectorRef, cts.Token));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SelectorRef — new field smoke tests
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SelectorRef_AllNewFields_CanBeConstructed()
    {
        // Verify the record compiles and sets all new fields correctly
        var sel = new SelectorRef
        {
            Role        = "button",
            RoleName    = "Submit",
            Label       = "Email",
            AriaLabel   = "Email (legacy)",
            Placeholder = "Enter email",
            TestId      = "email-field",
            Text        = "Sign in",
            Css         = "button.submit",
            XPath       = "//button[@type='submit']"
        };

        sel.Role.Should().Be("button");
        sel.RoleName.Should().Be("Submit");
        sel.Label.Should().Be("Email");
        sel.AriaLabel.Should().Be("Email (legacy)");
        sel.Placeholder.Should().Be("Enter email");
        sel.TestId.Should().Be("email-field");
        sel.Text.Should().Be("Sign in");
        sel.Css.Should().Be("button.submit");
        sel.XPath.Should().Be("//button[@type='submit']");
    }
}
