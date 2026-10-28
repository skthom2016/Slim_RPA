using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Moq;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Locators;
using TractoRuntime.Browser.Tests.Fixtures;

namespace TractoRuntime.Browser.Tests;

/// <summary>
/// Acceptance tests for BRW-US-008 — Frame and Shadow DOM Targeting.
///
/// Requirement refs: DOM-003, DOM-004
/// Acceptance tests:  BRW-AT-03, BRW-AT-04
///
/// Acceptance criteria:
///   AC-1  An element inside a nested iframe is located using an explicit frameChain array
///         of frame locators; the frame chain resolves within Playwright's permission model.
///   AC-2  After a frame navigates internally, locators are re-resolved against the new
///         frame context (Playwright lazy FrameLocator re-resolves on every action).
///   AC-3  An open shadow-root element is targeted using declared locators and works correctly.
///   AC-4  A closed or inaccessible shadow root fails explicitly with a structured error
///         (errorCategory=TARGET_NOT_FOUND, detailCode=browser.shadowRootClosed).
///   AC-5  Cross-origin iframe restrictions enforced by the browser remain in place;
///         the runtime does not attempt to bypass them.
/// </summary>
public sealed class BRW_US_008_Tests : BrowserTestBase
{
    private readonly FrameChainResolver _frameResolver =
        new(NullLogger<FrameChainResolver>.Instance);

    private readonly ShadowDomLocatorService _shadowService =
        new(NullLogger<ShadowDomLocatorService>.Instance);

    // ─── Helpers ────────────────────────────────────────────────────────────────

    private static Mock<IFrameLocator> MakeFrameLocatorWithLocator(Mock<ILocator> locatorMock)
    {
        var frameLocatorMock = new Mock<IFrameLocator>();
        frameLocatorMock
            .Setup(f => f.GetByRole(It.IsAny<AriaRole>(), It.IsAny<FrameLocatorGetByRoleOptions?>()))
            .Returns(locatorMock.Object);
        frameLocatorMock
            .Setup(f => f.GetByLabel(It.IsAny<string>(), It.IsAny<FrameLocatorGetByLabelOptions?>()))
            .Returns(locatorMock.Object);
        frameLocatorMock
            .Setup(f => f.GetByPlaceholder(It.IsAny<string>(), It.IsAny<FrameLocatorGetByPlaceholderOptions?>()))
            .Returns(locatorMock.Object);
        frameLocatorMock
            .Setup(f => f.GetByTestId(It.IsAny<string>()))
            .Returns(locatorMock.Object);
        frameLocatorMock
            .Setup(f => f.GetByText(It.IsAny<string>(), It.IsAny<FrameLocatorGetByTextOptions?>()))
            .Returns(locatorMock.Object);
        frameLocatorMock
            .Setup(f => f.Locator(It.IsAny<string>(), It.IsAny<FrameLocatorLocatorOptions?>()))
            .Returns(locatorMock.Object);
        return frameLocatorMock;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC-1 / BRW-AT-03 — Single-level iframe: element resolved inside frame
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AC1_SingleIframe_ByLabel_ReturnsLocatorScopedToFrame()
    {
        // Arrange
        var locatorMock = new Mock<ILocator>();
        var frameMock   = MakeFrameLocatorWithLocator(locatorMock);
        var pageMock    = new Mock<IPage>();

        pageMock
            .Setup(p => p.FrameLocator("iframe#payment"))
            .Returns(frameMock.Object);

        var selectorRef = new SelectorRef
        {
            FrameChain = [new FrameRef { Css = "iframe#payment" }],
            Label      = "Card number"
        };

        // Act
        var result = await _frameResolver.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);

        // Assert — AC-1: locator is the one from the frame scope, not the top-level page
        result.Should().BeSameAs(locatorMock.Object,
            because: "label locator inside single iframe must be scoped to that frame");

        pageMock.Verify(
            p => p.FrameLocator("iframe#payment"),
            Times.Once(),
            "the outer frame must be entered exactly once");
    }

    [Fact]
    public async Task AC1_SingleIframe_ByRole_ReturnsLocatorScopedToFrame()
    {
        // Arrange
        var locatorMock = new Mock<ILocator>();
        var frameMock   = MakeFrameLocatorWithLocator(locatorMock);
        var pageMock    = new Mock<IPage>();

        pageMock
            .Setup(p => p.FrameLocator("iframe#payment"))
            .Returns(frameMock.Object);

        var selectorRef = new SelectorRef
        {
            FrameChain = [new FrameRef { Css = "iframe#payment" }],
            Role       = "button",
            RoleName   = "Pay now"
        };

        // Act
        var result = await _frameResolver.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);

        // Assert
        result.Should().BeSameAs(locatorMock.Object,
            because: "role+name locator inside single iframe must resolve via FrameLocator.GetByRole");
    }

    [Fact]
    public async Task AC1_NestedIframeChain_TwoLevels_ResolvesBothFrames()
    {
        // Arrange — outer iframe contains an inner payment iframe
        var locatorMock    = new Mock<ILocator>();
        var innerFrameMock = MakeFrameLocatorWithLocator(locatorMock);
        var outerFrameMock = new Mock<IFrameLocator>();
        var pageMock       = new Mock<IPage>();

        pageMock
            .Setup(p => p.FrameLocator("iframe#outer"))
            .Returns(outerFrameMock.Object);

        outerFrameMock
            .Setup(f => f.FrameLocator("iframe#inner"))
            .Returns(innerFrameMock.Object);

        innerFrameMock
            .Setup(f => f.GetByTestId("card-number"))
            .Returns(locatorMock.Object);

        var selectorRef = new SelectorRef
        {
            FrameChain =
            [
                new FrameRef { Css = "iframe#outer" },
                new FrameRef { Css = "iframe#inner" }
            ],
            TestId = "card-number"
        };

        // Act
        var result = await _frameResolver.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);

        // Assert — AC-1: both levels of the chain are traversed
        result.Should().BeSameAs(locatorMock.Object,
            because: "nested two-level frame chain must produce a locator scoped to the innermost frame");

        pageMock.Verify(
            p => p.FrameLocator("iframe#outer"),
            Times.Once(),
            "outer frame must be entered once");

        outerFrameMock.Verify(
            f => f.FrameLocator("iframe#inner"),
            Times.Once(),
            "inner frame must be entered once from the outer frame locator");
    }

    [Fact]
    public async Task AC1_FrameRef_ByName_ProducesCorrectCssSelector()
    {
        // FrameRef.Name should produce iframe[name='...'] selector used in FrameLocator
        var locatorMock = new Mock<ILocator>();
        var frameMock   = MakeFrameLocatorWithLocator(locatorMock);
        var pageMock    = new Mock<IPage>();

        pageMock
            .Setup(p => p.FrameLocator("iframe[name='checkout']"))
            .Returns(frameMock.Object);

        var selectorRef = new SelectorRef
        {
            FrameChain = [new FrameRef { Name = "checkout" }],
            Text       = "Confirm"
        };

        // Act
        var result = await _frameResolver.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);

        // Assert
        result.Should().BeSameAs(locatorMock.Object,
            because: "FrameRef.Name must produce the iframe[name='...'] CSS selector");
    }

    [Fact]
    public async Task AC1_FrameRef_ByUrlPattern_ProducesCorrectCssSelector()
    {
        // Arrange
        var locatorMock = new Mock<ILocator>();
        var frameMock   = MakeFrameLocatorWithLocator(locatorMock);
        var pageMock    = new Mock<IPage>();

        pageMock
            .Setup(p => p.FrameLocator("iframe[src*='payments.example.com']"))
            .Returns(frameMock.Object);

        var selectorRef = new SelectorRef
        {
            FrameChain = [new FrameRef { UrlPattern = "payments.example.com" }],
            Label      = "CVV"
        };

        // Act
        var result = await _frameResolver.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);

        // Assert
        result.Should().BeSameAs(locatorMock.Object,
            because: "FrameRef.UrlPattern must produce the iframe[src*='...'] CSS selector");
    }

    [Fact]
    public async Task AC1_FrameRef_ByIndex1_ProducesNthOfType2CssSelector()
    {
        // zero-based Index=1 → CSS nth-of-type(2) (CSS is 1-based)
        var locatorMock = new Mock<ILocator>();
        var frameMock   = MakeFrameLocatorWithLocator(locatorMock);
        var pageMock    = new Mock<IPage>();

        pageMock
            .Setup(p => p.FrameLocator("iframe:nth-of-type(2)"))
            .Returns(frameMock.Object);

        var selectorRef = new SelectorRef
        {
            FrameChain = [new FrameRef { Index = 1 }],
            Css        = "button.pay"
        };

        // Act
        var result = await _frameResolver.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);

        // Assert
        result.Should().BeSameAs(locatorMock.Object,
            because: "FrameRef.Index=1 must produce iframe:nth-of-type(2) (CSS is 1-based)");
    }

    [Fact]
    public async Task AC1_FrameRef_ByIndex0_ProducesNthOfType1CssSelector()
    {
        // zero-based Index=0 → CSS nth-of-type(1)
        var locatorMock = new Mock<ILocator>();
        var frameMock   = MakeFrameLocatorWithLocator(locatorMock);
        var pageMock    = new Mock<IPage>();

        pageMock
            .Setup(p => p.FrameLocator("iframe:nth-of-type(1)"))
            .Returns(frameMock.Object);

        var selectorRef = new SelectorRef
        {
            FrameChain = [new FrameRef { Index = 0 }],
            Label      = "Amount"
        };

        var result = await _frameResolver.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);

        result.Should().BeSameAs(locatorMock.Object,
            because: "FrameRef.Index=0 must produce iframe:nth-of-type(1)");
    }

    [Fact]
    public async Task AC1_FrameRef_NameWithSingleQuote_EscapedInCssSelector()
    {
        // FrameRef.Name containing a single quote must be escaped in the CSS selector
        var locatorMock = new Mock<ILocator>();
        var frameMock   = MakeFrameLocatorWithLocator(locatorMock);
        var pageMock    = new Mock<IPage>();

        pageMock
            .Setup(p => p.FrameLocator("iframe[name='pay\\'ment']"))
            .Returns(frameMock.Object);

        var selectorRef = new SelectorRef
        {
            FrameChain = [new FrameRef { Name = "pay'ment" }],
            Label      = "Card"
        };

        var result = await _frameResolver.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);

        result.Should().BeSameAs(locatorMock.Object,
            because: "single quotes in frame name must be escaped to prevent CSS selector breakage");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC-1 negative — missing frame chain / missing element discriminator
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AC1_Negative_NoFrameChain_ThrowsArgumentException()
    {
        var pageMock    = new Mock<IPage>();
        var selectorRef = new SelectorRef { Label = "Email" }; // no FrameChain

        var act = () => _frameResolver.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>(
            because: "ResolveAsync must reject a SelectorRef without a frame chain");
    }

    [Fact]
    public async Task AC1_Negative_FrameChainButNoElementDiscriminator_ThrowsArgumentException()
    {
        var locatorMock = new Mock<ILocator>();
        var frameMock   = MakeFrameLocatorWithLocator(locatorMock);
        var pageMock    = new Mock<IPage>();

        pageMock.Setup(p => p.FrameLocator(It.IsAny<string>())).Returns(frameMock.Object);

        // SelectorRef has a frame chain but no element discriminator
        var selectorRef = new SelectorRef
        {
            FrameChain = [new FrameRef { Css = "iframe#payment" }]
            // intentionally no Role/Label/Placeholder/TestId/Text/Css/XPath
        };

        var act = () => _frameResolver.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>(
            because: "a frame chain with no element discriminator must be rejected");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC-2 / BRW-AT-03 — Re-resolution after frame navigation (Playwright lazy)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AC2_RepeatedResolve_ReturnsFreshLocatorBothTimes()
    {
        // AC-2: IFrameLocator is lazy — it re-resolves the underlying frame on every action.
        // Both calls must succeed and return a valid locator.
        var locatorMock = new Mock<ILocator>();
        var frameMock   = MakeFrameLocatorWithLocator(locatorMock);
        var pageMock    = new Mock<IPage>();

        pageMock
            .Setup(p => p.FrameLocator("iframe#widget"))
            .Returns(frameMock.Object);

        var selectorRef = new SelectorRef
        {
            FrameChain = [new FrameRef { Css = "iframe#widget" }],
            Label      = "Amount"
        };

        // Act — first resolution before hypothetical frame navigation
        var first  = await _frameResolver.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);
        // Act — second resolution after hypothetical frame navigation (frame re-resolves lazily)
        var second = await _frameResolver.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);

        // Assert — AC-2: both calls succeed; lazy re-resolution is Playwright's responsibility
        first.Should().NotBeNull(because: "first resolution must succeed");
        second.Should().NotBeNull(because: "second resolution (post-navigation) must also succeed");
    }

    // ─── Cancellation — CT is honoured before traversal ───────────────────────

    [Fact]
    public async Task AC2_CancelledToken_BeforeFrameTraversal_ThrowsOperationCanceledException()
    {
        var pageMock    = new Mock<IPage>();
        var selectorRef = new SelectorRef
        {
            FrameChain = [new FrameRef { Css = "iframe#payment" }],
            Label      = "Card number"
        };

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => _frameResolver.ResolveAsync(pageMock.Object, selectorRef, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            because: "a pre-cancelled token must be observed before any frame traversal begins");

        pageMock.Verify(
            p => p.FrameLocator(It.IsAny<string>()),
            Times.Never(),
            "FrameLocator must not be called when the token is already cancelled");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC-3 / BRW-AT-04 — Open shadow-root element targeted correctly
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AC3_ShadowRoot_ByLabel_BuildsLocatorSuccessfully()
    {
        var locatorMock = new Mock<ILocator>();
        var pageMock    = new Mock<IPage>();

        pageMock
            .Setup(p => p.GetByLabel("Card number", It.IsAny<PageGetByLabelOptions?>()))
            .Returns(locatorMock.Object);

        var selectorRef = new SelectorRef
        {
            ShadowRoot = true,
            Label      = "Card number"
        };

        // Act
        var result = _shadowService.BuildLocator(pageMock.Object, selectorRef);

        // Assert — AC-3: locator is built successfully; Playwright pierces open shadow roots
        result.Should().BeSameAs(locatorMock.Object,
            because: "GetByLabel pierces open shadow roots automatically in Playwright");
    }

    [Fact]
    public void AC3_ShadowRoot_ByRole_BuildsLocatorSuccessfully()
    {
        var locatorMock = new Mock<ILocator>();
        var pageMock    = new Mock<IPage>();

        pageMock
            .Setup(p => p.GetByRole(AriaRole.Button, It.Is<PageGetByRoleOptions>(o => o.Name == "Pay")))
            .Returns(locatorMock.Object);

        var selectorRef = new SelectorRef
        {
            ShadowRoot = true,
            Role       = "button",
            RoleName   = "Pay"
        };

        var result = _shadowService.BuildLocator(pageMock.Object, selectorRef);

        result.Should().BeSameAs(locatorMock.Object,
            because: "GetByRole pierces open shadow roots automatically in Playwright");
    }

    [Fact]
    public void AC3_ShadowRoot_ByTestId_BuildsLocatorSuccessfully()
    {
        var locatorMock = new Mock<ILocator>();
        var pageMock    = new Mock<IPage>();

        pageMock.Setup(p => p.GetByTestId("pay-button")).Returns(locatorMock.Object);

        var selectorRef = new SelectorRef { ShadowRoot = true, TestId = "pay-button" };

        var result = _shadowService.BuildLocator(pageMock.Object, selectorRef);

        result.Should().BeSameAs(locatorMock.Object,
            because: "GetByTestId pierces open shadow roots automatically in Playwright");
    }

    [Fact]
    public void AC3_ShadowRoot_ByCss_BuildsLocatorSuccessfully()
    {
        var locatorMock = new Mock<ILocator>();
        var pageMock    = new Mock<IPage>();

        pageMock.Setup(p => p.Locator("my-widget .submit-btn", It.IsAny<PageLocatorOptions?>()))
                .Returns(locatorMock.Object);

        var selectorRef = new SelectorRef { ShadowRoot = true, Css = "my-widget .submit-btn" };

        var result = _shadowService.BuildLocator(pageMock.Object, selectorRef);

        result.Should().BeSameAs(locatorMock.Object,
            because: "CSS locators also pierce open shadow roots via Playwright/CDP");
    }

    [Fact]
    public void AC3_ShadowRoot_ByPlaceholder_BuildsLocatorSuccessfully()
    {
        var locatorMock = new Mock<ILocator>();
        var pageMock    = new Mock<IPage>();

        pageMock.Setup(p => p.GetByPlaceholder("Card number", It.IsAny<PageGetByPlaceholderOptions?>()))
                .Returns(locatorMock.Object);

        var selectorRef = new SelectorRef { ShadowRoot = true, Placeholder = "Card number" };

        var result = _shadowService.BuildLocator(pageMock.Object, selectorRef);

        result.Should().BeSameAs(locatorMock.Object,
            because: "GetByPlaceholder pierces open shadow roots automatically in Playwright");
    }

    [Fact]
    public void AC3_ShadowRoot_ByText_BuildsLocatorSuccessfully()
    {
        var locatorMock = new Mock<ILocator>();
        var pageMock    = new Mock<IPage>();

        pageMock.Setup(p => p.GetByText("Submit", It.IsAny<PageGetByTextOptions?>()))
                .Returns(locatorMock.Object);

        var selectorRef = new SelectorRef { ShadowRoot = true, Text = "Submit" };

        var result = _shadowService.BuildLocator(pageMock.Object, selectorRef);

        result.Should().BeSameAs(locatorMock.Object,
            because: "GetByText pierces open shadow roots automatically in Playwright");
    }

    // ─── AC-3 negative: BuildLocator rejects ShadowRoot=false ───────────────

    [Fact]
    public void AC3_Negative_ShadowRootFalse_ThrowsArgumentException()
    {
        var pageMock    = new Mock<IPage>();
        var selectorRef = new SelectorRef { ShadowRoot = false, Label = "Email" };

        var act = () => _shadowService.BuildLocator(pageMock.Object, selectorRef);

        act.Should().Throw<ArgumentException>(
            because: "BuildLocator must only be called with ShadowRoot=true");
    }

    [Fact]
    public void AC3_Negative_ShadowRootTrue_NoDiscriminator_ThrowsArgumentException()
    {
        var pageMock    = new Mock<IPage>();
        var selectorRef = new SelectorRef { ShadowRoot = true }; // no discriminator

        var act = () => _shadowService.BuildLocator(pageMock.Object, selectorRef);

        act.Should().Throw<ArgumentException>(
            because: "ShadowRoot=true with no element discriminator must be rejected");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC-4 / BRW-AT-04 — Closed/inaccessible shadow root → structured error
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AC4_ClosedShadowRoot_ExplicitShadowMessage_ReturnsStructuredError()
    {
        // Arrange — PlaywrightException message contains "closed shadow"
        var ex          = new PlaywrightException("Timeout 30000ms exceeded. closed shadow root.");
        var selectorRef = new SelectorRef { ShadowRoot = true, Label = "Card number" };

        // Act
        var error = _shadowService.TryClassifyShadowDomError(ex, selectorRef);

        // Assert — AC-4: classified as TARGET_NOT_FOUND / browser.shadowRootClosed
        error.Should().NotBeNull(because: "a closed-shadow-root message must be classified");
        error!.Category.Should().Be(ErrorCategory.TARGET_NOT_FOUND,
            because: "closed shadow root maps to errorCategory=TARGET_NOT_FOUND per AC-4");
        error.DetailCode.Should().Be("browser.shadowRootClosed",
            because: "the required detail code is browser.shadowRootClosed per spec");
    }

    [Fact]
    public void AC4_ClosedShadowRoot_TimeoutOnShadowRootSelector_ReturnsStructuredError()
    {
        // Generic timeout on a shadowRoot=true selector implies closed/inaccessible root
        var ex          = new PlaywrightException("Timeout 30000ms exceeded waiting for locator.");
        var selectorRef = new SelectorRef { ShadowRoot = true, TestId = "card-input" };

        var error = _shadowService.TryClassifyShadowDomError(ex, selectorRef);

        error.Should().NotBeNull(
            because: "a timeout on ShadowRoot=true implies a closed/inaccessible shadow root");
        error!.Category.Should().Be(ErrorCategory.TARGET_NOT_FOUND);
        error.DetailCode.Should().Be("browser.shadowRootClosed");
    }

    [Fact]
    public void AC4_NonShadowRoot_PlaywrightException_ReturnsNull()
    {
        // ShadowRoot=false — the error is not shadow-related
        var ex          = new PlaywrightException("Timeout 30000ms exceeded waiting for locator.");
        var selectorRef = new SelectorRef { ShadowRoot = false, Label = "Email" };

        var error = _shadowService.TryClassifyShadowDomError(ex, selectorRef);

        error.Should().BeNull(
            because: "TryClassifyShadowDomError must return null when ShadowRoot=false");
    }

    [Fact]
    public void AC4_ShadowRoot_NonTimeoutError_ShadowRootFragment_StillClassified()
    {
        // Message contains "shadowroot" keyword (e.g. DevTools access error)
        var ex          = new PlaywrightException("Cannot access shadowroot of closed element.");
        var selectorRef = new SelectorRef { ShadowRoot = true, Css = ".widget" };

        var error = _shadowService.TryClassifyShadowDomError(ex, selectorRef);

        error.Should().NotBeNull();
        error!.DetailCode.Should().Be("browser.shadowRootClosed");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC-5 — Cross-origin frame: the runtime does not bypass browser restrictions
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AC5_CrossOriginFrame_RuntimeDoesNotBypassRestrictions()
    {
        // AC-5: the runtime only calls the standard Playwright FrameLocator API;
        // it does NOT inject scripts, override CSP, or use any CDP bypass mechanism.
        var locatorMock = new Mock<ILocator>();
        var frameMock   = MakeFrameLocatorWithLocator(locatorMock);
        var pageMock    = new Mock<IPage>();

        // page.FrameLocator is called with the declared CSS selector — no bypass attempt
        pageMock
            .Setup(p => p.FrameLocator("iframe[src*='payments.example.com']"))
            .Returns(frameMock.Object);

        var selectorRef = new SelectorRef
        {
            FrameChain = [new FrameRef { UrlPattern = "payments.example.com" }],
            Label      = "Card number"
        };

        // Act — runtime builds the locator; Playwright enforces cross-origin rules at action time
        var result = await _frameResolver.ResolveAsync(pageMock.Object, selectorRef, CancellationToken.None);

        result.Should().NotBeNull(because: "frame chain resolution must succeed for same-origin frames");

        // Verify no bypass APIs were called
        pageMock.Verify(
            p => p.EvaluateAsync(It.IsAny<string>(), It.IsAny<object>()),
            Times.Never(),
            "the runtime must never call page.evaluate to bypass cross-origin restrictions (AC-5)");

        pageMock.Verify(
            p => p.AddInitScriptAsync(It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never(),
            "the runtime must never inject init scripts to bypass cross-origin restrictions (AC-5)");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SelectorRef.HasFrameChain — property boundary conditions
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void HasFrameChain_NullFrameChain_ReturnsFalse()
    {
        var selectorRef = new SelectorRef { Label = "Email" };
        selectorRef.HasFrameChain.Should().BeFalse(
            because: "a null FrameChain must report HasFrameChain=false");
    }

    [Fact]
    public void HasFrameChain_EmptyFrameChain_ReturnsFalse()
    {
        var selectorRef = new SelectorRef { FrameChain = [], Label = "Email" };
        selectorRef.HasFrameChain.Should().BeFalse(
            because: "an empty FrameChain list must report HasFrameChain=false");
    }

    [Fact]
    public void HasFrameChain_OneEntry_ReturnsTrue()
    {
        var selectorRef = new SelectorRef
        {
            FrameChain = [new FrameRef { Css = "iframe#payment" }],
            Label      = "Card"
        };
        selectorRef.HasFrameChain.Should().BeTrue(
            because: "a FrameChain with one entry must report HasFrameChain=true");
    }
}
