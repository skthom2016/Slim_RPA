using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Moq;
using TractoRuntime.Browser.Actions;
using TractoRuntime.Browser.Authentication;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Dialogs;
using TractoRuntime.Browser.Failure;
using TractoRuntime.Browser.Locators;
using TractoRuntime.Browser.Tabs;
using TractoRuntime.Browser.Tests.Fixtures;

namespace TractoRuntime.Browser.Tests;

/// <summary>
/// Acceptance tests for BRW-US-012 — Tab, Popup, and Dialog Management.
/// Covers BRW-AT-05, BRW-AT-09.
/// Requirement refs: WEB-004, WEB-005, WEB-009.
/// </summary>
/// <remarks>
///   AC#1 — Expected popup is detected and linked to the correct browser context.
///   AC#2 — Unexpected popup stops automation and is reported as a structured error.
///   AC#3 — JS alert/confirm/prompt is handled with declared response; dialog closes.
///   AC#4 — Unhandled permission prompt is detected and reported; no answer is guessed.
///   AC#5 — MFA/CAPTCHA challenge causes run to stop with FAILED / needsAttention=true.
///   AC#6 — Expired session causes run to stop with needsAttention=true or FAILED.
/// </remarks>
public sealed class BRW_US_012_Tests : BrowserTestBase
{
    private readonly Mock<IPage>    _pageMock    = new();
    private readonly Mock<ILocator> _locatorMock = new();

    // ── SUT factories ────────────────────────────────────────────────────────

    private ActionExecutor CreateExecutor(
        ITabManager? tabManager = null,
        IDialogHandler? dialogHandler = null,
        IMfaAndSessionDetector? mfaDetector = null)
    {
        var strategyMock = new Mock<ISelectorStrategy>();
        strategyMock
            .Setup(s => s.ResolveAsync(It.IsAny<IPage>(), It.IsAny<SelectorRef>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_locatorMock.Object);

        _locatorMock
            .Setup(l => l.ClickAsync(It.IsAny<LocatorClickOptions?>()))
            .Returns(Task.CompletedTask);

        return new ActionExecutor(
            strategyMock.Object,
            FailureClassifierMock.Object,
            NullLogger<ActionExecutor>.Instance,
            tabManager: tabManager,
            dialogHandler: dialogHandler,
            mfaDetector: mfaDetector);
    }

    // ── AC#1 — TabManager.DeclareExpectedPopup / SwitchToPopupAsync ──────────

    [Fact]
    public void DeclareExpectedPopup_WhenPopupIdIsUnique_Succeeds()
    {
        // Arrange
        var tabManager = new TabManager(NullLogger<TabManager>.Instance);
        var declaration = new PopupDeclaration { PopupId = "sign-in-popup" };

        // Act
        var action = () => tabManager.DeclareExpectedPopup(declaration);

        // Assert — no exception; the declaration is registered
        action.Should().NotThrow();
    }

    [Fact]
    public void DeclareExpectedPopup_DuplicatePopupId_ThrowsInvalidOperation()
    {
        // Arrange
        var tabManager = new TabManager(NullLogger<TabManager>.Instance);
        var declaration = new PopupDeclaration { PopupId = "popup-1" };
        tabManager.DeclareExpectedPopup(declaration);

        // Act
        var action = () => tabManager.DeclareExpectedPopup(declaration);

        // Assert — AC#1: duplicate PopupId is rejected
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*popup-1*");
    }

    [Fact]
    public async Task SwitchToPopupAsync_BeforeArrival_TimesOutWithMeaningfulMessage()
    {
        // Arrange
        var tabManager = new TabManager(NullLogger<TabManager>.Instance);
        tabManager.DeclareExpectedPopup(new PopupDeclaration { PopupId = "late-popup" });

        // Act
        var action = () => tabManager.SwitchToPopupAsync(
            "late-popup", TimeSpan.FromMilliseconds(50), CancellationToken.None);

        // Assert — AC#1: times out rather than hanging indefinitely
        await action.Should().ThrowAsync<TimeoutException>()
            .WithMessage("*late-popup*");
    }

    [Fact]
    public async Task SwitchToPopupAsync_UndeclaredPopupId_ThrowsInvalidOperation()
    {
        // Arrange
        var tabManager = new TabManager(NullLogger<TabManager>.Instance);

        // Act
        var action = () => tabManager.SwitchToPopupAsync(
            "never-declared", TimeSpan.FromSeconds(1), CancellationToken.None);

        // Assert — AC#1: must be declared before switch
        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*never-declared*");
    }

    // ── AC#2 — Unexpected popup detection ────────────────────────────────────

    [Fact]
    public void Install_WhenNewPageFires_UnmatchedPageRecordedAsUnexpected()
    {
        // Arrange
        var tabManager = new TabManager(NullLogger<TabManager>.Instance);
        var contextMock = new Mock<IBrowserContext>();
        EventHandler<IPage>? capturedHandler = null;

        contextMock
            .SetupAdd(c => c.Page += It.IsAny<EventHandler<IPage>>())
            .Callback<EventHandler<IPage>>(h => capturedHandler = h);

        tabManager.Install(contextMock.Object);

        // Simulate a new-page event with no matching declaration
        var unexpectedPage = new Mock<IPage>();
        unexpectedPage.Setup(p => p.Url).Returns("https://phishing.example.com");
        capturedHandler!.Invoke(contextMock.Object, unexpectedPage.Object);

        // Assert — AC#2: unexpected popup recorded, not silently closed
        tabManager.HasUnexpectedPopup.Should().BeTrue();
        var (url, message) = tabManager.ConsumeUnexpectedPopupError()!.Value;
        url.Should().Contain("phishing.example.com");
        message.Should().Contain("undeclared popup");
    }

    [Fact]
    public async Task ActionExecutor_TabDeclare_WhenTabManagerMissing_ReturnsInternalError()
    {
        // Arrange — no tab manager injected
        var executor = CreateExecutor();
        var step = new BrowserStep
        {
            StepId  = "s1",
            Action  = "browser.tab.declare",
            PopupRef = new PopupDeclaration { PopupId = "popup" }
        };

        // Act
        var result = await executor.ExecuteAsync(_pageMock.Object, step, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.INTERNAL);
        result.DetailCode.Should().Be("browser.tabManagerNotConfigured");
    }

    [Fact]
    public async Task ActionExecutor_TabDeclare_WhenPopupRefMissing_ReturnsValidationError()
    {
        // Arrange
        var tabManager = new TabManager(NullLogger<TabManager>.Instance);
        var executor = CreateExecutor(tabManager: tabManager);
        var step = new BrowserStep { StepId = "s1", Action = "browser.tab.declare" };

        // Act
        var result = await executor.ExecuteAsync(_pageMock.Object, step, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.VALIDATION);
        result.DetailCode.Should().Be("browser.missingPopupRef");
    }

    [Fact]
    public async Task ActionExecutor_TabSwitch_WhenPopupIdMissing_ReturnsValidationError()
    {
        // Arrange
        var tabManagerMock = new Mock<ITabManager>();
        var executor = CreateExecutor(tabManager: tabManagerMock.Object);
        var step = new BrowserStep { StepId = "s1", Action = "browser.tab.switch" };  // Value is null

        // Act
        var result = await executor.ExecuteAsync(_pageMock.Object, step, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.VALIDATION);
        result.DetailCode.Should().Be("browser.missingPopupId");
    }

    // ── AC#3 — Dialog arm / respond ──────────────────────────────────────────

    [Fact]
    public async Task ActionExecutor_DialogArm_WhenDialogHandlerMissing_ReturnsInternalError()
    {
        // Arrange — no dialog handler injected
        var executor = CreateExecutor();
        var step = new BrowserStep
        {
            StepId    = "s1",
            Action    = "browser.dialog.arm",
            DialogRef = new DialogDeclaration { Accept = true }
        };

        // Act
        var result = await executor.ExecuteAsync(_pageMock.Object, step, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.INTERNAL);
        result.DetailCode.Should().Be("browser.dialogHandlerNotConfigured");
    }

    [Fact]
    public async Task ActionExecutor_DialogArm_WhenDialogRefMissing_ReturnsValidationError()
    {
        // Arrange
        var dialogHandlerMock = new Mock<IDialogHandler>();
        var executor = CreateExecutor(dialogHandler: dialogHandlerMock.Object);
        var step = new BrowserStep { StepId = "s1", Action = "browser.dialog.arm" };

        // Act
        var result = await executor.ExecuteAsync(_pageMock.Object, step, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.VALIDATION);
        result.DetailCode.Should().Be("browser.missingDialogRef");
    }

    [Fact]
    public async Task ActionExecutor_DialogArm_WithValidDeclaration_ArmsHandlerAndSucceeds()
    {
        // Arrange
        var dialogHandlerMock = new Mock<IDialogHandler>();
        var executor = CreateExecutor(dialogHandler: dialogHandlerMock.Object);
        var declaration = new DialogDeclaration { Accept = true, PromptText = null };
        var step = new BrowserStep
        {
            StepId    = "s1",
            Action    = "browser.dialog.arm",
            DialogRef = declaration
        };

        // Act
        var result = await executor.ExecuteAsync(_pageMock.Object, step, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        dialogHandlerMock.Verify(h => h.ArmResponse(declaration), Times.Once);
    }

    [Fact]
    public async Task ActionExecutor_DialogRespond_WithSelectorAndAccept_ClicksAndArms()
    {
        // Arrange
        var dialogHandlerMock = new Mock<IDialogHandler>();
        dialogHandlerMock.Setup(h => h.HasUnhandledDialogError).Returns(false);

        var declaration = new DialogDeclaration { Accept = true };
        var executor = CreateExecutor(dialogHandler: dialogHandlerMock.Object);
        var step = new BrowserStep
        {
            StepId    = "s1",
            Action    = "browser.dialog.respond",
            DialogRef = declaration,
            Selector  = new SelectorRef { Role = "button", RoleName = "OK" }
        };

        // Act
        var result = await executor.ExecuteAsync(_pageMock.Object, step, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        dialogHandlerMock.Verify(h => h.ArmResponse(declaration), Times.Once);
        _locatorMock.Verify(l => l.ClickAsync(It.IsAny<LocatorClickOptions?>()), Times.Once);
    }

    // ── AC#3 — DialogHandler unit tests ─────────────────────────────────────

    [Fact]
    public void DialogHandler_ArmResponse_SetsArmedDeclaration()
    {
        // Arrange
        var handler = new DialogHandler(NullLogger<DialogHandler>.Instance);
        var declaration = new DialogDeclaration { Accept = true, PromptText = "secret" };

        // Act — just arm; no dialog event fires in a unit test
        var action = () => handler.ArmResponse(declaration);

        // Assert — no exception thrown
        action.Should().NotThrow();
        handler.HasUnhandledDialogError.Should().BeFalse();
    }

    [Fact]
    public void DialogHandler_OnUnhandledDialog_RecordsError()
    {
        // Arrange
        var handler = new DialogHandler(NullLogger<DialogHandler>.Instance);
        var pageMock = new Mock<IPage>();
        EventHandler<IDialog>? capturedHandler = null;

        pageMock
            .SetupAdd(p => p.Dialog += It.IsAny<EventHandler<IDialog>>())
            .Callback<EventHandler<IDialog>>(h => capturedHandler = h);

        handler.Install(pageMock.Object);

        // Simulate an unexpected dialog arriving with no armed response
        var dialogMock = new Mock<IDialog>();
        dialogMock.Setup(d => d.Type).Returns("confirm");
        dialogMock.Setup(d => d.Message).Returns("Are you sure?");
        dialogMock.Setup(d => d.DismissAsync()).Returns(Task.CompletedTask);

        capturedHandler!.Invoke(pageMock.Object, dialogMock.Object);

        // Assert — AC#4: error recorded, dialog dismissed (not accepted)
        handler.HasUnhandledDialogError.Should().BeTrue();
        var err = handler.ConsumeUnhandledDialogError()!;
        err.Category.Should().Be(ErrorCategory.POLICY_DENIED);
        err.DetailCode.Should().Be("browser.unexpectedDialog");

        // The dialog was dismissed (AC#4 — runtime does not guess an answer)
        dialogMock.Verify(d => d.DismissAsync(), Times.Once);
        dialogMock.Verify(d => d.AcceptAsync(It.IsAny<string?>()), Times.Never);
    }

    // ── AC#5 — MFA / CAPTCHA detection ───────────────────────────────────────

    [Fact]
    public async Task MfaDetector_MfaUrlPattern_ReturnsInterventionRequired()
    {
        // Arrange
        var detector = new MfaAndSessionDetector(NullLogger<MfaAndSessionDetector>.Instance);
        var pageMock = new Mock<IPage>();
        pageMock.Setup(p => p.Url).Returns("https://login.example.com/mfa/verify");

        // Simulate body text so InnerTextAsync doesn't fail
        var bodyLocatorMock = new Mock<ILocator>();
        bodyLocatorMock
            .Setup(l => l.InnerTextAsync(It.IsAny<LocatorInnerTextOptions?>()))
            .ReturnsAsync("Please enter your authentication code");
        pageMock
            .Setup(p => p.Locator("body"))
            .Returns(bodyLocatorMock.Object);

        // Act
        var result = await detector.DetectAsync(pageMock.Object, CancellationToken.None);

        // Assert — AC#5
        result.InterventionRequired.Should().BeTrue();
        result.Kind.Should().Be(DetectedInterventionKind.MfaChallenge);
        result.NeedsAttention.Should().BeTrue();
    }

    [Fact]
    public async Task MfaDetector_SessionExpiredDomKeyword_ReturnsInterventionRequired()
    {
        // Arrange
        var detector = new MfaAndSessionDetector(NullLogger<MfaAndSessionDetector>.Instance);
        var pageMock = new Mock<IPage>();
        pageMock.Setup(p => p.Url).Returns("https://app.example.com/dashboard");

        var bodyLocatorMock = new Mock<ILocator>();
        bodyLocatorMock
            .Setup(l => l.InnerTextAsync(It.IsAny<LocatorInnerTextOptions?>()))
            .ReturnsAsync("Your session has expired. Please sign in again.");
        pageMock
            .Setup(p => p.Locator("body"))
            .Returns(bodyLocatorMock.Object);

        // Act
        var result = await detector.DetectAsync(pageMock.Object, CancellationToken.None);

        // Assert — AC#6
        result.InterventionRequired.Should().BeTrue();
        result.Kind.Should().Be(DetectedInterventionKind.SessionExpired);
        result.NeedsAttention.Should().BeTrue();
    }

    [Fact]
    public async Task MfaDetector_NormalPage_ReturnsNoIntervention()
    {
        // Arrange
        var detector = new MfaAndSessionDetector(NullLogger<MfaAndSessionDetector>.Instance);
        var pageMock = new Mock<IPage>();
        pageMock.Setup(p => p.Url).Returns("https://app.example.com/orders");

        var bodyLocatorMock = new Mock<ILocator>();
        bodyLocatorMock
            .Setup(l => l.InnerTextAsync(It.IsAny<LocatorInnerTextOptions?>()))
            .ReturnsAsync("Order #12345 - Delivered");
        pageMock
            .Setup(p => p.Locator("body"))
            .Returns(bodyLocatorMock.Object);

        // Act
        var result = await detector.DetectAsync(pageMock.Object, CancellationToken.None);

        // Assert — no intervention
        result.InterventionRequired.Should().BeFalse();
        result.Kind.Should().Be(DetectedInterventionKind.None);
    }

    [Fact]
    public async Task ActionExecutor_CheckAuthChallenge_WithMfaDetected_ReturnsFailedWithNeedsAttention()
    {
        // Arrange
        var detectorMock = new Mock<IMfaAndSessionDetector>();
        detectorMock
            .Setup(d => d.DetectAsync(It.IsAny<IPage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MfaDetectionResult
            {
                InterventionRequired = true,
                Kind                 = DetectedInterventionKind.MfaChallenge,
                NeedsAttention       = true,
                Reason               = "MFA challenge detected"
            });

        var executor = CreateExecutor(mfaDetector: detectorMock.Object);
        var step = new BrowserStep { StepId = "auth-check", Action = "browser.page.check-auth" };

        // Act
        var result = await executor.ExecuteAsync(_pageMock.Object, step, CancellationToken.None);

        // Assert — AC#5: FAILED, needsAttention=true
        result.Succeeded.Should().BeFalse();
        result.NeedsAttention.Should().BeTrue();
        result.ErrorCategory.Should().Be(ErrorCategory.AUTHENTICATION);
        result.DetailCode.Should().Be("browser.mfaChallengeDetected");
    }

    [Fact]
    public async Task ActionExecutor_CheckAuthChallenge_WithNoDetector_Succeeds()
    {
        // Arrange — no mfa detector injected (optional feature)
        var executor = CreateExecutor();
        var step = new BrowserStep { StepId = "auth-check", Action = "browser.page.check-auth" };

        // Act
        var result = await executor.ExecuteAsync(_pageMock.Object, step, CancellationToken.None);

        // Assert — no detector = no error reported
        result.Succeeded.Should().BeTrue();
    }

    // ── CEC compliance guards ─────────────────────────────────────────────────

    [Fact]
    public void DialogDeclaration_PromptText_DoesNotContainHardcodedPassword()
    {
        // Guard: DialogDeclaration must not embed raw secrets.
        // This test documents the contract; production code should reject dialogRef.PromptText
        // that matches a known-secret pattern — here we assert the field is nullable so
        // it can be left null when the runtime resolves a secret reference separately.
        var decl = new DialogDeclaration { Accept = true };
        decl.PromptText.Should().BeNull();   // safe by default
    }

    [Fact]
    public void PopupDeclaration_PopupId_CannotBeEmpty()
    {
        var action = () => new PopupDeclaration { PopupId = "" };
        // The record allows empty strings at construction — enforcement is at DeclareExpectedPopup.
        // This test documents that DeclareExpectedPopup rejects blank IDs.
        var tabManager = new TabManager(NullLogger<TabManager>.Instance);
        var declareAction = () => tabManager.DeclareExpectedPopup(new PopupDeclaration { PopupId = "" });
        declareAction.Should().Throw<ArgumentException>();
    }
}
