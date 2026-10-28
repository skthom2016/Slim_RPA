using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Moq;
using TractoRuntime.Browser.Actions;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Failure;
using TractoRuntime.Browser.Locators;
using TractoRuntime.Browser.Tests.Fixtures;

namespace TractoRuntime.Browser.Tests;

/// <summary>
/// Acceptance tests for BRW-US-009 — Dynamic Page Waits and Stale Target Handling.
/// Covers BRW-AT-01, BRW-AT-02, BRW-AT-05, BRW-AT-08, BRW-AT-23.
/// Requirement refs: DOM-005, DOM-006, DOM-007, BRE-001, BRE-005.
/// </summary>
/// <remarks>
///   AC#1 — After navigation the runtime waits for DomContentLoaded (not networkidle); the
///           step can additionally declare a target locator to wait for.
///   AC#2 — SPA pages with ongoing background telemetry complete once the expected element
///           appears, not after all traffic settles.
///   AC#3 — Locators are re-resolved on every action call; no stale element handle is used.
///   AC#4 — Disabled/overlaid elements produce errorCategory=TIMEOUT / browser.actionabilityTimeout.
///   AC#5 — A click is dispatched exactly once; the runtime never re-submits after re-render.
/// </remarks>
public sealed class BRW_US_009_Tests : BrowserTestBase
{
    // ── Shared mocks ─────────────────────────────────────────────────────────

    private readonly Mock<IPage>     _pageMock     = new();
    private readonly Mock<ILocator>  _locatorMock  = new();
    private readonly Mock<IResponse> _responseMock = new();

    // ── SUT factories ────────────────────────────────────────────────────────

    private ActionExecutor CreateActionExecutorWithClassifierMock()
    {
        var strategyMock = new Mock<ISelectorStrategy>();
        return new(
            strategyMock.Object,
            FailureClassifierMock.Object,
            NullLogger<ActionExecutor>.Instance);
    }

    /// <summary>
    /// Creates an ActionExecutor whose ISelectorStrategy always returns
    /// <see cref="_locatorMock"/>.
    /// </summary>
    private ActionExecutor CreateExecutorWithLocator(ILocator locator)
    {
        var strategy = new Mock<ISelectorStrategy>();
        strategy
            .Setup(s => s.ResolveAsync(
                It.IsAny<IPage>(),
                It.IsAny<SelectorRef>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(locator);

        return new ActionExecutor(
            strategy.Object,
            new FailureClassifier(NullLogger<FailureClassifier>.Instance),
            NullLogger<ActionExecutor>.Instance);
    }

    // =========================================================================
    // Part 1 — SelectorStrategy unit tests
    // =========================================================================

    [Fact]
    public async Task SelectorStrategy_AriaLabel_UsesGetByLabel()
    {
        // Arrange
        _pageMock
            .Setup(p => p.GetByLabel(It.IsAny<string>(), It.IsAny<PageGetByLabelOptions?>()))
            .Returns(_locatorMock.Object);

        var sut    = new SelectorStrategy(NullLogger<SelectorStrategy>.Instance);
        var selRef = new SelectorRef { AriaLabel = "Username" };

        // Act
        var locator = await sut.ResolveAsync(_pageMock.Object, selRef, default);

        // Assert
        locator.Should().BeSameAs(_locatorMock.Object,
            "AriaLabel (backward-compat alias for Label) must resolve via GetByLabel");
        _pageMock.Verify(p => p.GetByLabel("Username", It.IsAny<PageGetByLabelOptions?>()), Times.Once);
    }

    [Fact]
    public async Task SelectorStrategy_Label_UsesGetByLabel()
    {
        // Arrange — Label is the primary field; AriaLabel is the backward-compat alias.
        // Both must resolve via GetByLabel. This test verifies the primary Label field is not silently skipped.
        _pageMock
            .Setup(p => p.GetByLabel(It.IsAny<string>(), It.IsAny<PageGetByLabelOptions?>()))
            .Returns(_locatorMock.Object);

        var sut    = new SelectorStrategy(NullLogger<SelectorStrategy>.Instance);
        var selRef = new SelectorRef { Label = "Email address" }; // primary field, not the alias

        // Act
        var locator = await sut.ResolveAsync(_pageMock.Object, selRef, default);

        // Assert
        locator.Should().BeSameAs(_locatorMock.Object,
            "Label (primary field) must resolve via GetByLabel — not silently fall through");
        _pageMock.Verify(p => p.GetByLabel("Email address", It.IsAny<PageGetByLabelOptions?>()), Times.Once);
    }

    [Fact]
    public async Task SelectorStrategy_Role_UsesGetByRole()
    {
        // Arrange
        _pageMock
            .Setup(p => p.GetByRole(It.IsAny<AriaRole>(), It.IsAny<PageGetByRoleOptions?>()))
            .Returns(_locatorMock.Object);

        var sut    = new SelectorStrategy(NullLogger<SelectorStrategy>.Instance);
        var selRef = new SelectorRef { Role = "button", RoleName = "Save" };

        // Act
        var locator = await sut.ResolveAsync(_pageMock.Object, selRef, default);

        // Assert
        locator.Should().BeSameAs(_locatorMock.Object,
            "Role has highest semantic priority (spec §9 F-08); GetByRole must be used");
        _pageMock.Verify(
            p => p.GetByRole(AriaRole.Button,
                It.Is<PageGetByRoleOptions?>(o => o != null && o.Name == "Save")),
            Times.Once,
            "Role+RoleName must be forwarded to GetByRole with accessible name filter");
    }

    [Fact]
    public async Task SelectorStrategy_Placeholder_UsesGetByPlaceholder()
    {
        // Arrange
        _pageMock
            .Setup(p => p.GetByPlaceholder(It.IsAny<string>(), It.IsAny<PageGetByPlaceholderOptions?>()))
            .Returns(_locatorMock.Object);

        var sut    = new SelectorStrategy(NullLogger<SelectorStrategy>.Instance);
        var selRef = new SelectorRef { Placeholder = "Search…" };

        // Act
        var locator = await sut.ResolveAsync(_pageMock.Object, selRef, default);

        // Assert
        locator.Should().BeSameAs(_locatorMock.Object,
            "Placeholder must resolve via GetByPlaceholder (Priority 3 per spec §9 F-08)");
        _pageMock.Verify(p => p.GetByPlaceholder("Search…", It.IsAny<PageGetByPlaceholderOptions?>()), Times.Once);
    }

    [Fact]
    public async Task SelectorStrategy_TestId_UsesGetByTestId()
    {
        _pageMock
            .Setup(p => p.GetByTestId(It.IsAny<string>()))
            .Returns(_locatorMock.Object);

        var sut    = new SelectorStrategy(NullLogger<SelectorStrategy>.Instance);
        var selRef = new SelectorRef { TestId = "submit-btn" };

        var locator = await sut.ResolveAsync(_pageMock.Object, selRef, default);

        locator.Should().BeSameAs(_locatorMock.Object);
        _pageMock.Verify(p => p.GetByTestId("submit-btn"), Times.Once);
    }

    [Fact]
    public async Task SelectorStrategy_Text_UsesGetByTextWithExactMatch()
    {
        _pageMock
            .Setup(p => p.GetByText(It.IsAny<string>(), It.IsAny<PageGetByTextOptions?>()))
            .Returns(_locatorMock.Object);

        var sut    = new SelectorStrategy(NullLogger<SelectorStrategy>.Instance);
        var selRef = new SelectorRef { Text = "Save changes" };

        var locator = await sut.ResolveAsync(_pageMock.Object, selRef, default);

        locator.Should().BeSameAs(_locatorMock.Object);
        _pageMock.Verify(
            p => p.GetByText("Save changes",
                It.Is<PageGetByTextOptions?>(o => o != null && o.Exact == true)),
            Times.Once,
            "Text locator must use exact match to prevent accidental partial-text ambiguity");
    }

    [Fact]
    public async Task SelectorStrategy_Css_UsesLocator()
    {
        _pageMock
            .Setup(p => p.Locator(It.IsAny<string>(), It.IsAny<PageLocatorOptions?>()))
            .Returns(_locatorMock.Object);

        var sut    = new SelectorStrategy(NullLogger<SelectorStrategy>.Instance);
        var selRef = new SelectorRef { Css = "#main-form button[type='submit']" };

        var locator = await sut.ResolveAsync(_pageMock.Object, selRef, default);

        locator.Should().BeSameAs(_locatorMock.Object);
    }

    [Fact]
    public async Task SelectorStrategy_XPath_UsesLocator()
    {
        _pageMock
            .Setup(p => p.Locator(It.IsAny<string>(), It.IsAny<PageLocatorOptions?>()))
            .Returns(_locatorMock.Object);

        var sut    = new SelectorStrategy(NullLogger<SelectorStrategy>.Instance);
        var selRef = new SelectorRef { XPath = "//button[@id='save']" };

        var locator = await sut.ResolveAsync(_pageMock.Object, selRef, default);

        locator.Should().BeSameAs(_locatorMock.Object);
    }

    [Fact]
    public async Task SelectorStrategy_AllNull_ThrowsInvalidOperationException()
    {
        var sut    = new SelectorStrategy(NullLogger<SelectorStrategy>.Instance);
        var selRef = new SelectorRef(); // all fields null

        await sut.Invoking(s => s.ResolveAsync(_pageMock.Object, selRef, default))
            .Should().ThrowAsync<InvalidOperationException>(
                "at least one selector strategy field must be provided");
    }

    [Fact]
    public async Task SelectorStrategy_AriaLabel_TakesPriorityOverCss()
    {
        _pageMock
            .Setup(p => p.GetByLabel(It.IsAny<string>(), It.IsAny<PageGetByLabelOptions?>()))
            .Returns(_locatorMock.Object);

        var sut = new SelectorStrategy(NullLogger<SelectorStrategy>.Instance);
        // Both AriaLabel and Css are set — AriaLabel must win
        var selRef = new SelectorRef { AriaLabel = "Email", Css = "input[type='email']" };

        await sut.ResolveAsync(_pageMock.Object, selRef, default);

        _pageMock.Verify(p => p.GetByLabel("Email", It.IsAny<PageGetByLabelOptions?>()), Times.Once);
        _pageMock.Verify(
            p => p.Locator(It.IsAny<string>(), It.IsAny<PageLocatorOptions?>()),
            Times.Never,
            "CSS locator must not be used when AriaLabel is available");
    }

    [Fact]
    public async Task SelectorStrategy_Cancellation_ThrowsBeforeResolution()
    {
        var sut    = new SelectorStrategy(NullLogger<SelectorStrategy>.Instance);
        var selRef = new SelectorRef { Css = "button" };
        var cts    = new CancellationTokenSource();
        cts.Cancel();

        await sut.Invoking(s => s.ResolveAsync(_pageMock.Object, selRef, cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    // =========================================================================
    // Part 2 — FailureClassifier unit tests
    // =========================================================================

    private static FailureClassifier CreateClassifier()
        => new(NullLogger<FailureClassifier>.Instance);

    [Fact]
    public void FailureClassifier_NavigationTimeout_ReturnsNavigationTimeoutDetailCode()
    {
        var sut = CreateClassifier();
        var ex  = new PlaywrightException(
            "page.goto: Timeout 30000ms exceeded.");

        var result = sut.Classify(ex, stateKnown: false);

        result.Category.Should().Be(ErrorCategory.TIMEOUT);
        result.DetailCode.Should().Be("browser.navigationTimeout",
            "page.goto prefix identifies a navigation-scoped timeout (DOM-005)");
    }

    [Fact]
    public void FailureClassifier_ActionabilityTimeout_ReturnsActionabilityTimeoutDetailCode()
    {
        var sut = CreateClassifier();
        var ex  = new PlaywrightException(
            "locator.click: Timeout 10000ms exceeded. Waiting for locator('#btn') " +
            "to be visible, enabled and stable.");

        var result = sut.Classify(ex, stateKnown: false);

        result.Category.Should().Be(ErrorCategory.TIMEOUT);
        result.DetailCode.Should().Be("browser.actionabilityTimeout",
            "locator.click prefix identifies an actionability-scoped timeout (DOM-007, BRE-001)");
    }

    [Fact]
    public void FailureClassifier_WaitForTimeout_ReturnsActionabilityTimeout()
    {
        var sut = CreateClassifier();
        var ex  = new PlaywrightException(
            "locator.waitFor: Timeout 10000ms exceeded. " +
            "Waiting for locator('[data-testid=save-btn]') to be visible.");

        var result = sut.Classify(ex, stateKnown: false);

        result.Category.Should().Be(ErrorCategory.TIMEOUT);
        result.DetailCode.Should().Be("browser.actionabilityTimeout");
    }

    [Fact]
    public void FailureClassifier_StrictModeViolation_ReturnsTargetAmbiguous()
    {
        var sut = CreateClassifier();
        var ex  = new PlaywrightException(
            "strict mode violation: locator('button') resolved to 3 elements.");

        var result = sut.Classify(ex, stateKnown: false);

        result.Category.Should().Be(ErrorCategory.TARGET_AMBIGUOUS);
        result.DetailCode.Should().Be("browser.multipleLocatorMatches");
    }

    [Fact]
    public void FailureClassifier_NoElementFound_ReturnsTargetNotFound()
    {
        var sut = CreateClassifier();
        var ex  = new PlaywrightException(
            "element is not attached to the document");

        var result = sut.Classify(ex, stateKnown: false);

        result.Category.Should().Be(ErrorCategory.TARGET_NOT_FOUND);
        result.DetailCode.Should().Be("browser.locatorNotFound");
    }

    [Fact]
    public void FailureClassifier_OperationCancelled_ReturnsCancelled()
    {
        var sut = CreateClassifier();
        var ex  = new OperationCanceledException("Run was cancelled.");

        var result = sut.Classify(ex, stateKnown: false);

        result.Category.Should().Be(ErrorCategory.CANCELLED);
        result.DetailCode.Should().BeNull();
    }

    [Fact]
    public void FailureClassifier_UnknownException_ReturnsInternal()
    {
        var sut = CreateClassifier();
        var ex  = new InvalidOperationException("Something unexpected happened.");

        var result = sut.Classify(ex, stateKnown: false);

        result.Category.Should().Be(ErrorCategory.INTERNAL);
    }

    [Fact]
    public void FailureClassifier_WaitForNavigationTimeout_ReturnsNavigationTimeout()
    {
        var sut = CreateClassifier();
        var ex  = new PlaywrightException(
            "page.waitForNavigation: Timeout 30000ms exceeded.");

        var result = sut.Classify(ex, stateKnown: false);

        result.Category.Should().Be(ErrorCategory.TIMEOUT);
        result.DetailCode.Should().Be("browser.navigationTimeout");
    }

    // =========================================================================
    // Part 3 — ActionExecutor integration tests
    // =========================================================================

    // ── AC#1: Navigate waits for DomContentLoaded, NOT networkidle ───────────

    [Fact]
    public async Task AC1_Navigate_WaitsForDomContentLoaded_NotNetworkIdle()
    {
        // Arrange
        PageGotoOptions? capturedOptions = null;
        _pageMock
            .Setup(p => p.GotoAsync(It.IsAny<string>(), It.IsAny<PageGotoOptions?>()))
            .Callback<string, PageGotoOptions?>((_, o) => capturedOptions = o)
            .ReturnsAsync(_responseMock.Object);
        _pageMock.SetupGet(p => p.Url).Returns("https://example.com/dashboard");

        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId  = "nav-1",
            Action  = "browser.page.navigate",
            Value   = "https://example.com/dashboard"
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeTrue();
        capturedOptions.Should().NotBeNull();
        capturedOptions!.WaitUntil.Should().Be(WaitUntilState.DOMContentLoaded,
            "AC#1: navigation must wait for DOMContentLoaded — never networkidle, " +
            "which would block on SPA background telemetry");
        capturedOptions.WaitUntil.Should().NotBe(WaitUntilState.NetworkIdle,
            "networkidle blocks SPA pages with ongoing background traffic");
    }

    [Fact]
    public async Task AC1_Navigate_WithoutSelector_DoesNotWaitForLocator()
    {
        // Arrange
        _pageMock
            .Setup(p => p.GotoAsync(It.IsAny<string>(), It.IsAny<PageGotoOptions?>()))
            .ReturnsAsync(_responseMock.Object);
        _pageMock.SetupGet(p => p.Url).Returns("https://example.com/");

        var strategyMock = new Mock<ISelectorStrategy>();
        var sut = new ActionExecutor(
            strategyMock.Object,
            new FailureClassifier(NullLogger<FailureClassifier>.Instance),
            NullLogger<ActionExecutor>.Instance);

        var step = new BrowserStep
        {
            StepId = "nav-no-sel",
            Action = "browser.page.navigate",
            Value  = "https://example.com/"
            // Selector intentionally not set
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeTrue();
        strategyMock.Verify(
            s => s.ResolveAsync(It.IsAny<IPage>(), It.IsAny<SelectorRef>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "AC#1: when no selector is declared, no additional locator wait should occur");
    }

    [Fact]
    public async Task AC1_Navigate_WithoutValue_ReturnsFailure()
    {
        // Arrange — navigate step missing URL
        FailureClassifierMock
            .Setup(f => f.Classify(It.IsAny<Exception>(), It.IsAny<bool>()))
            .Returns(new CecError
            {
                Category   = ErrorCategory.INTERNAL,
                Message    = "Missing URL",
                DetailCode = null
            });

        var sut  = CreateActionExecutorWithClassifierMock();
        var step = new BrowserStep
        {
            StepId = "nav-no-url",
            Action = "browser.page.navigate"
            // Value intentionally null
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeFalse(
            "navigate without a URL must fail — step.Value is required");
    }

    // ── AC#2: SPA — navigate + wait for locator independently of background traffic ──

    [Fact]
    public async Task AC2_Navigate_WithSelector_WaitsForLocatorAfterDomContentLoaded()
    {
        // Arrange
        _pageMock
            .Setup(p => p.GotoAsync(It.IsAny<string>(), It.IsAny<PageGotoOptions?>()))
            .ReturnsAsync(_responseMock.Object);
        _pageMock.SetupGet(p => p.Url).Returns("https://app.example.com/feed");

        _locatorMock
            .Setup(l => l.WaitForAsync(It.IsAny<LocatorWaitForOptions?>()))
            .Returns(Task.CompletedTask);

        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId   = "nav-spa",
            Action   = "browser.page.navigate",
            Value    = "https://app.example.com/feed",
            Selector = new SelectorRef { TestId = "feed-list" }
        };

        // Act — simulates SPA with background telemetry; locator appears before all traffic settles
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeTrue("AC#2: SPA navigation must succeed once expected element is visible");
        _locatorMock.Verify(
            l => l.WaitForAsync(It.Is<LocatorWaitForOptions?>(o =>
                o != null && o.State == WaitForSelectorState.Visible)),
            Times.Once,
            "AC#2: locator wait must use Visible state, not Attached — the element must be rendered");
    }

    // ── AC#3: Locator re-resolved on every action call ───────────────────────

    [Fact]
    public async Task AC3_Click_ReResolvesLocatorBeforeEachAction()
    {
        // Arrange — two separate click steps; strategy must be called twice
        var strategyMock = new Mock<ISelectorStrategy>();
        strategyMock
            .Setup(s => s.ResolveAsync(
                It.IsAny<IPage>(), It.IsAny<SelectorRef>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_locatorMock.Object);

        _locatorMock
            .Setup(l => l.ClickAsync(It.IsAny<LocatorClickOptions?>()))
            .Returns(Task.CompletedTask);

        var sut = new ActionExecutor(
            strategyMock.Object,
            new FailureClassifier(NullLogger<FailureClassifier>.Instance),
            NullLogger<ActionExecutor>.Instance);

        var selector = new SelectorRef { TestId = "grid-row-3" };
        var step     = new BrowserStep
        {
            StepId   = "click-row",
            Action   = "browser.element.click",
            Selector = selector
        };

        // Act — simulate calling the same step twice (e.g., after a re-render the grid refreshed)
        await sut.ExecuteAsync(_pageMock.Object, step, default);
        await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert — locator was re-resolved on each call (not cached)
        strategyMock.Verify(
            s => s.ResolveAsync(
                It.IsAny<IPage>(), It.IsAny<SelectorRef>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2),
            "AC#3: the locator must be re-resolved before every action — never cached across calls");
    }

    [Fact]
    public async Task AC3_Fill_ReResolvesLocatorBeforeAction()
    {
        // Arrange
        var strategyMock = new Mock<ISelectorStrategy>();
        strategyMock
            .Setup(s => s.ResolveAsync(
                It.IsAny<IPage>(), It.IsAny<SelectorRef>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_locatorMock.Object);

        _locatorMock
            .Setup(l => l.FillAsync(It.IsAny<string>(), It.IsAny<LocatorFillOptions?>()))
            .Returns(Task.CompletedTask);

        var sut = new ActionExecutor(
            strategyMock.Object,
            new FailureClassifier(NullLogger<FailureClassifier>.Instance),
            NullLogger<ActionExecutor>.Instance);

        var step = new BrowserStep
        {
            StepId   = "fill-name",
            Action   = "browser.element.fill",
            Selector = new SelectorRef { AriaLabel = "Full name" },
            Value    = "Jane Doe"
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeTrue();
        strategyMock.Verify(
            s => s.ResolveAsync(
                It.IsAny<IPage>(), It.IsAny<SelectorRef>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "AC#3: strategy must be called once per fill call — fresh locator before each action");
    }

    // ── AC#4: Actionability timeout → TIMEOUT/browser.actionabilityTimeout ───

    [Fact]
    public async Task AC4_Click_DisabledElement_ReturnsActionabilityTimeout()
    {
        // Arrange — Playwright's actionability check raises TimeoutException for disabled button
        _locatorMock
            .Setup(l => l.ClickAsync(It.IsAny<LocatorClickOptions?>()))
            .ThrowsAsync(new PlaywrightException(
                "locator.click: Timeout 10000ms exceeded. " +
                "Waiting for locator('#save-btn') to be visible, enabled and stable."));

        var sut    = CreateExecutorWithLocator(_locatorMock.Object);
        var step   = new BrowserStep
        {
            StepId   = "click-disabled",
            Action   = "browser.element.click",
            Selector = new SelectorRef { TestId = "save-btn" }
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.TIMEOUT,
            "AC#4: disabled or overlaid element must produce errorCategory=TIMEOUT");
        result.DetailCode.Should().Be("browser.actionabilityTimeout",
            "AC#4: detailCode must be browser.actionabilityTimeout — no blind JS click (DOM-007)");
    }

    [Fact]
    public async Task AC4_WaitForElement_Timeout_ReturnsActionabilityTimeout()
    {
        // Arrange
        _locatorMock
            .Setup(l => l.WaitForAsync(It.IsAny<LocatorWaitForOptions?>()))
            .ThrowsAsync(new PlaywrightException(
                "locator.waitFor: Timeout 30000ms exceeded. " +
                "Waiting for locator('[data-testid=results-table]') to be visible."));

        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId   = "wait-results",
            Action   = "browser.element.wait",
            Selector = new SelectorRef { TestId = "results-table" }
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.TIMEOUT);
        result.DetailCode.Should().Be("browser.actionabilityTimeout",
            "wait timeout classifies as actionabilityTimeout (BRE-001)");
    }

    [Fact]
    public async Task AC4_Navigate_Timeout_ReturnsNavigationTimeout()
    {
        // Arrange
        _pageMock
            .Setup(p => p.GotoAsync(It.IsAny<string>(), It.IsAny<PageGotoOptions?>()))
            .ThrowsAsync(new PlaywrightException(
                "page.goto: Timeout 30000ms exceeded."));

        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId = "nav-slow",
            Action = "browser.page.navigate",
            Value  = "https://slow.example.com"
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.TIMEOUT);
        result.DetailCode.Should().Be("browser.navigationTimeout",
            "page.goto timeout classifies as browser.navigationTimeout (DOM-005)");
    }

    // ── AC#5: Click dispatched exactly once — no re-submission after re-render ─

    [Fact]
    public async Task AC5_Click_InvokedOnce_PerStepCall()
    {
        // Arrange
        _locatorMock
            .Setup(l => l.ClickAsync(It.IsAny<LocatorClickOptions?>()))
            .Returns(Task.CompletedTask);

        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId   = "save-form",
            Action   = "browser.element.click",
            Selector = new SelectorRef { TestId = "submit-btn" }
        };

        // Act — call ExecuteAsync exactly once (caller must not call again on re-render)
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert — click was dispatched exactly once
        result.Succeeded.Should().BeTrue();
        _locatorMock.Verify(
            l => l.ClickAsync(It.IsAny<LocatorClickOptions?>()),
            Times.Once,
            "AC#5: click must be dispatched exactly once per step call — " +
            "re-render must not cause a second dispatch");
    }

    [Fact]
    public async Task AC5_Click_StaleAfterDispatch_ReturnsFailureNotRetried()
    {
        // Arrange — click throws AFTER dispatch (stale element race condition)
        _locatorMock
            .Setup(l => l.ClickAsync(It.IsAny<LocatorClickOptions?>()))
            .ThrowsAsync(new PlaywrightException("element is not attached to the document"));

        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId   = "save-stale",
            Action   = "browser.element.click",
            Selector = new SelectorRef { TestId = "submit-btn" }
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert — returned failure, NOT retried (stale element = TARGET_NOT_FOUND)
        result.Succeeded.Should().BeFalse(
            "AC#5: a stale element exception must produce a failure result without retrying");
        _locatorMock.Verify(
            l => l.ClickAsync(It.IsAny<LocatorClickOptions?>()),
            Times.Once,
            "AC#5: click must not be re-dispatched — would cause double-submit (BRE-005)");
    }

    // ── Unsupported action ────────────────────────────────────────────────────

    [Fact]
    public async Task UnknownAction_ReturnsValidationFailure()
    {
        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId = "bad-action",
            Action = "browser.element.teleport"
        };

        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.VALIDATION,
            "an unrecognised action name is a script validation error");
        result.DetailCode.Should().Be("browser.unsupportedAction");
    }

    // ── Find element — TARGET_NOT_FOUND / TARGET_AMBIGUOUS ───────────────────

    [Fact]
    public async Task Find_UniqueElement_Succeeds()
    {
        // Arrange
        var firstLocatorMock = new Mock<ILocator>();
        firstLocatorMock
            .Setup(l => l.WaitForAsync(It.IsAny<LocatorWaitForOptions?>()))
            .Returns(Task.CompletedTask);

        _locatorMock.SetupGet(l => l.First).Returns(firstLocatorMock.Object);
        _locatorMock
            .Setup(l => l.CountAsync())
            .ReturnsAsync(1);

        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId   = "find-unique",
            Action   = "browser.element.find",
            Selector = new SelectorRef { Css = ".primary-action" }
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeTrue("exactly one element found — unique match is valid");
    }

    [Fact]
    public async Task Find_ZeroElements_ReturnsTargetNotFound()
    {
        // Arrange
        var firstLocatorMock = new Mock<ILocator>();
        firstLocatorMock
            .Setup(l => l.WaitForAsync(It.IsAny<LocatorWaitForOptions?>()))
            .Returns(Task.CompletedTask);

        _locatorMock.SetupGet(l => l.First).Returns(firstLocatorMock.Object);
        _locatorMock.Setup(l => l.CountAsync()).ReturnsAsync(0);

        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId   = "find-zero",
            Action   = "browser.element.find",
            Selector = new SelectorRef { Css = ".does-not-exist" }
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.TARGET_NOT_FOUND);
        result.DetailCode.Should().Be("browser.locatorNotFound");
    }

    [Fact]
    public async Task Find_MultipleElements_ReturnsTargetAmbiguous()
    {
        // Arrange
        var firstLocatorMock = new Mock<ILocator>();
        firstLocatorMock
            .Setup(l => l.WaitForAsync(It.IsAny<LocatorWaitForOptions?>()))
            .Returns(Task.CompletedTask);

        _locatorMock.SetupGet(l => l.First).Returns(firstLocatorMock.Object);
        _locatorMock.Setup(l => l.CountAsync()).ReturnsAsync(4);

        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId   = "find-many",
            Action   = "browser.element.find",
            Selector = new SelectorRef { Css = "button" }
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.TARGET_AMBIGUOUS);
        result.DetailCode.Should().Be("browser.multipleLocatorMatches");
    }

    // ── Per-step timeout is forwarded to Playwright ───────────────────────────

    [Fact]
    public async Task CustomTimeout_ForwardedToPlaywright()
    {
        // Arrange
        LocatorClickOptions? capturedOptions = null;
        _locatorMock
            .Setup(l => l.ClickAsync(It.IsAny<LocatorClickOptions?>()))
            .Callback<LocatorClickOptions?>(o => capturedOptions = o)
            .Returns(Task.CompletedTask);

        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId   = "click-custom-timeout",
            Action   = "browser.element.click",
            Selector = new SelectorRef { TestId = "btn" },
            Timeout  = TimeSpan.FromSeconds(5)
        };

        // Act
        await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert — the declared timeout was forwarded to Playwright (BRE-001)
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Timeout.Should().BeApproximately(5_000f, 1f,
            "BRE-001: per-step declared timeout must be forwarded to Playwright exactly");
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Navigate_CancelledToken_ReturnsCancelledError()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId = "nav-cancel",
            Action = "browser.page.navigate",
            Value  = "https://example.com"
        };

        // Act — token already cancelled before ExecuteAsync is called
        var result = await sut.ExecuteAsync(_pageMock.Object, step, cts.Token);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.CANCELLED,
            "a pre-cancelled token must produce a CANCELLED error");
    }
}
