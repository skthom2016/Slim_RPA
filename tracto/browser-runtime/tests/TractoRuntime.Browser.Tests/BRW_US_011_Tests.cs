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
/// Acceptance tests for BRW-US-011 — Form Filling, Typing, and Pointer Actions.
/// Covers BRW-AT-01, BRW-AT-02, BRW-AT-05.
/// Requirement refs: WEB-002, WEB-003.
/// </summary>
/// <remarks>
///   AC#1 — Text input, textarea, and contenteditable fields are filled with the declared
///           value, including Unicode; the filled value is read back where supported.
///   AC#2 — &lt;select&gt; dropdown and checkbox are set to the declared option/state and
///           the resulting selection is verified.
///   AC#3 — Click, double-click, hover, scroll, and keyboard shortcut each produce exactly
///           one intended event after a dynamic layout shift.
///   AC#4 — Disabled inputs produce errorCategory=TIMEOUT / browser.actionabilityTimeout
///           — no silent dispatch.
///   AC#5 — Multilingual form submits with exact Unicode field values.
///   AC#6 — Supported drag-and-drop produces the intended drag result; an unsupported drag
///           target or out-of-bounds drag reports errorCategory=TIMEOUT (actionability).
/// </remarks>
public sealed class BRW_US_011_Tests : BrowserTestBase
{
    // ── Shared mocks ─────────────────────────────────────────────────────────

    private readonly Mock<IPage>    _pageMock    = new();
    private readonly Mock<ILocator> _locatorMock = new();

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an <see cref="ActionExecutor"/> whose <see cref="ISelectorStrategy"/>
    /// always returns <see cref="_locatorMock"/> for any ResolveAsync call.
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

    /// <summary>
    /// Creates an executor whose strategy returns <paramref name="sourceLocator"/> for the
    /// first ResolveAsync call and <paramref name="targetLocator"/> for the second.
    /// Used for drag-and-drop tests where source and target differ.
    /// </summary>
    private ActionExecutor CreateExecutorWithTwoLocators(
        ILocator sourceLocator, ILocator targetLocator)
    {
        var strategy  = new Mock<ISelectorStrategy>();
        var callCount = 0;
        strategy
            .Setup(s => s.ResolveAsync(
                It.IsAny<IPage>(),
                It.IsAny<SelectorRef>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1 ? sourceLocator : targetLocator;
            });

        return new ActionExecutor(
            strategy.Object,
            new FailureClassifier(NullLogger<FailureClassifier>.Instance),
            NullLogger<ActionExecutor>.Instance);
    }

    // =========================================================================
    // AC#1 — Fill / Type (WEB-002)
    // =========================================================================

    [Fact]
    public async Task AC1_Fill_TextInput_Succeeds()
    {
        // Arrange — simulate a standard text input accepting FillAsync
        _locatorMock
            .Setup(l => l.FillAsync(It.IsAny<string>(), It.IsAny<LocatorFillOptions?>()))
            .Returns(Task.CompletedTask);
        // AC#1: read-back after fill — InputValueAsync returns the value that was set
        _locatorMock
            .Setup(l => l.InputValueAsync(It.IsAny<LocatorInputValueOptions?>()))
            .ReturnsAsync("Müller, Jörg");

        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId   = "fill-name",
            Action   = "browser.element.fill",
            Selector = new SelectorRef { Label = "Full name" },
            Value    = "Müller, Jörg"     // Unicode — umlauts (AC#5)
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeTrue("fill on a valid text input must succeed");
        _locatorMock.Verify(
            l => l.FillAsync("Müller, Jörg", It.IsAny<LocatorFillOptions?>()),
            Times.Once,
            "AC#1: the declared Unicode value must be forwarded to FillAsync exactly once");
        result.Evidence.Should().ContainKey("filledValue",
            "AC#1: the filled value must be read back via InputValueAsync and recorded in evidence");
        result.Evidence["filledValue"].Should().Be("Müller, Jörg",
            "AC#1: the read-back value must equal the declared fill value");
    }

    [Fact]
    public async Task AC1_Type_ContentEditable_UsesSequentialKeyEvents()
    {
        // Arrange — contenteditable field requires PressSequentiallyAsync (key events)
        _locatorMock
            .Setup(l => l.PressSequentiallyAsync(It.IsAny<string>(), It.IsAny<LocatorPressSequentiallyOptions?>()))
            .Returns(Task.CompletedTask);

        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId   = "type-editor",
            Action   = "browser.element.type",
            Selector = new SelectorRef { Role = "textbox", RoleName = "Notes" },
            Value    = "Hello, 世界"   // Unicode CJK characters (AC#1, AC#5)
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeTrue("type on a contenteditable field must succeed");
        _locatorMock.Verify(
            l => l.PressSequentiallyAsync(
                "Hello, 世界",
                It.IsAny<LocatorPressSequentiallyOptions?>()),
            Times.Once,
            "AC#1: PressSequentiallyAsync must be used for contenteditable fields");
    }

    [Fact]
    public async Task AC1_Type_MissingSelectorRequiredByAction_ReturnsFailure()
    {
        // Arrange — no classifier mock needed; the guard in TypeSequentiallyAsync throws
        //           synchronously and is caught by ExecuteAsync
        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId = "type-no-sel",
            Action = "browser.element.type",
            Value  = "some text"
            // Selector intentionally absent
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeFalse(
            "browser.element.type without a selector must produce a failure result");
    }

    // =========================================================================
    // AC#2 — Select dropdown / checkbox (WEB-002)
    // =========================================================================

    [Fact]
    public async Task AC2_SelectOption_SetsDropdownToCorrectValue_AndVerifiesSelection()
    {
        // Arrange — SelectOptionAsync returns the selected values list
        _locatorMock
            .Setup(l => l.SelectOptionAsync(
                It.IsAny<string>(),
                It.IsAny<LocatorSelectOptionOptions?>()))
            .ReturnsAsync(["CA"]);

        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId   = "select-province",
            Action   = "browser.element.select",
            Selector = new SelectorRef { Label = "Province" },
            Value    = "CA"
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeTrue("selecting a valid option must succeed");
        result.Evidence.Should().ContainKey("selectedValues",
            "AC#2: selected values must be captured in evidence for verification");
        _locatorMock.Verify(
            l => l.SelectOptionAsync("CA", It.IsAny<LocatorSelectOptionOptions?>()),
            Times.Once,
            "AC#2: the option value must be passed to SelectOptionAsync exactly once");
    }

    [Fact]
    public async Task AC2_CheckElement_SetsCheckedAndVerifiesState()
    {
        // Arrange — simulate checkbox that can be checked; read-back returns true
        _locatorMock
            .Setup(l => l.CheckAsync(It.IsAny<LocatorCheckOptions?>()))
            .Returns(Task.CompletedTask);
        _locatorMock
            .Setup(l => l.IsCheckedAsync(It.IsAny<LocatorIsCheckedOptions?>()))
            .ReturnsAsync(true);

        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId   = "accept-terms",
            Action   = "browser.element.check",
            Selector = new SelectorRef { Label = "I accept the terms" }
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeTrue("checking an enabled checkbox must succeed");
        result.Evidence.Should().ContainKey("isChecked",
            "AC#2: checked state must be read back and recorded in evidence");
        result.Evidence["isChecked"].Should().Be(true,
            "AC#2: read-back value must confirm the checkbox is now checked");
    }

    [Fact]
    public async Task AC2_UncheckElement_SetsUncheckedAndVerifiesState()
    {
        // Arrange — uncheck a previously-checked box; read-back returns false
        _locatorMock
            .Setup(l => l.UncheckAsync(It.IsAny<LocatorUncheckOptions?>()))
            .Returns(Task.CompletedTask);
        _locatorMock
            .Setup(l => l.IsCheckedAsync(It.IsAny<LocatorIsCheckedOptions?>()))
            .ReturnsAsync(false);

        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId   = "opt-out",
            Action   = "browser.element.uncheck",
            Selector = new SelectorRef { Label = "Subscribe to newsletter" }
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeTrue("unchecking an enabled checkbox must succeed");
        result.Evidence["isChecked"].Should().Be(false,
            "AC#2: read-back must confirm the checkbox is now unchecked");
    }

    // =========================================================================
    // AC#3 — Pointer / keyboard actions (WEB-003)
    // =========================================================================

    [Fact]
    public async Task AC3_DblClick_DispatchedExactlyOnce()
    {
        // Arrange
        _locatorMock
            .Setup(l => l.DblClickAsync(It.IsAny<LocatorDblClickOptions?>()))
            .Returns(Task.CompletedTask);

        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId   = "rename-file",
            Action   = "browser.element.dblclick",
            Selector = new SelectorRef { TestId = "file-row-1" }
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeTrue("double-click on an actionable element must succeed");
        _locatorMock.Verify(
            l => l.DblClickAsync(It.IsAny<LocatorDblClickOptions?>()),
            Times.Once,
            "AC#3: double-click must be dispatched exactly once per step call");
    }

    [Fact]
    public async Task AC3_Hover_DispatchedExactlyOnce()
    {
        // Arrange
        _locatorMock
            .Setup(l => l.HoverAsync(It.IsAny<LocatorHoverOptions?>()))
            .Returns(Task.CompletedTask);

        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId   = "hover-tooltip",
            Action   = "browser.element.hover",
            Selector = new SelectorRef { TestId = "info-icon" }
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeTrue("hover on a visible element must succeed");
        _locatorMock.Verify(
            l => l.HoverAsync(It.IsAny<LocatorHoverOptions?>()),
            Times.Once,
            "AC#3: hover must be dispatched exactly once per step call");
    }

    [Fact]
    public async Task AC3_Scroll_ElementScrolledIntoView()
    {
        // Arrange
        _locatorMock
            .Setup(l => l.ScrollIntoViewIfNeededAsync(It.IsAny<LocatorScrollIntoViewIfNeededOptions?>()))
            .Returns(Task.CompletedTask);

        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId   = "scroll-footer",
            Action   = "browser.element.scroll",
            Selector = new SelectorRef { TestId = "footer-cta" }
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeTrue("scroll must succeed when element is attached");
        _locatorMock.Verify(
            l => l.ScrollIntoViewIfNeededAsync(It.IsAny<LocatorScrollIntoViewIfNeededOptions?>()),
            Times.Once,
            "AC#3: ScrollIntoViewIfNeededAsync must be called exactly once");
    }

    [Fact]
    public async Task AC3_KeyboardShortcut_PressedExactlyOnce()
    {
        // Arrange — mock the IKeyboard
        var keyboardMock = new Mock<IKeyboard>();
        keyboardMock
            .Setup(k => k.PressAsync(It.IsAny<string>(), It.IsAny<KeyboardPressOptions?>()))
            .Returns(Task.CompletedTask);
        _pageMock.SetupGet(p => p.Keyboard).Returns(keyboardMock.Object);

        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId = "save-shortcut",
            Action = "browser.keyboard.shortcut",
            Value  = "Control+S"
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeTrue("keyboard shortcut on a focused page must succeed");
        keyboardMock.Verify(
            k => k.PressAsync("Control+S", It.IsAny<KeyboardPressOptions?>()),
            Times.Once,
            "AC#3: the declared key combo must be pressed exactly once");
    }

    [Fact]
    public async Task AC3_KeyboardShortcut_MissingValue_ReturnsFailure()
    {
        // Arrange — step with no Value (key combo)
        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId = "shortcut-no-key",
            Action = "browser.keyboard.shortcut"
            // Value intentionally absent
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeFalse(
            "browser.keyboard.shortcut without a Value must fail — the key combo is required");
    }

    // =========================================================================
    // AC#4 — Disabled / non-interactive controls (WEB-002, WEB-003)
    // =========================================================================

    [Fact]
    public async Task AC4_Fill_DisabledInput_ReturnsActionabilityTimeout()
    {
        // Arrange — Playwright raises TimeoutException when editable check fails on disabled input
        _locatorMock
            .Setup(l => l.FillAsync(It.IsAny<string>(), It.IsAny<LocatorFillOptions?>()))
            .ThrowsAsync(new PlaywrightException(
                "locator.fill: Timeout 30000ms exceeded. " +
                "Waiting for locator('[aria-label=Amount]') to be editable."));

        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId   = "fill-disabled",
            Action   = "browser.element.fill",
            Selector = new SelectorRef { AriaLabel = "Amount" },
            Value    = "99.99"
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.TIMEOUT,
            "AC#4: a disabled input must produce errorCategory=TIMEOUT — no silent dispatch");
        result.DetailCode.Should().Be("browser.actionabilityTimeout",
            "AC#4: detailCode must be browser.actionabilityTimeout per spec (WEB-002)");
    }

    [Fact]
    public async Task AC4_Check_DisabledCheckbox_ReturnsActionabilityTimeout()
    {
        // Arrange — disabled checkbox raises TimeoutException from Playwright's actionability check
        _locatorMock
            .Setup(l => l.CheckAsync(It.IsAny<LocatorCheckOptions?>()))
            .ThrowsAsync(new PlaywrightException(
                "locator.check: Timeout 10000ms exceeded. " +
                "Waiting for locator('[data-testid=opt-in]') to be enabled."));

        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId   = "check-disabled",
            Action   = "browser.element.check",
            Selector = new SelectorRef { TestId = "opt-in" }
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.TIMEOUT,
            "AC#4: disabled checkbox must produce TIMEOUT — no silent state change");
        result.DetailCode.Should().Be("browser.actionabilityTimeout");
    }

    [Fact]
    public async Task AC4_Select_DisabledDropdown_ReturnsActionabilityTimeout()
    {
        // Arrange
        _locatorMock
            .Setup(l => l.SelectOptionAsync(
                It.IsAny<string>(),
                It.IsAny<LocatorSelectOptionOptions?>()))
            .ThrowsAsync(new PlaywrightException(
                "locator.selectOption: Timeout 10000ms exceeded. " +
                "Waiting for locator('[data-testid=country-select]') to be enabled."));

        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId   = "select-disabled",
            Action   = "browser.element.select",
            Selector = new SelectorRef { TestId = "country-select" },
            Value    = "DE"
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.TIMEOUT,
            "AC#4: a disabled <select> must produce TIMEOUT (actionability) — not a silent pass");
        result.DetailCode.Should().Be("browser.actionabilityTimeout");
    }

    // =========================================================================
    // AC#5 — Multilingual form — Unicode fill (WEB-002)
    // =========================================================================

    [Fact]
    public async Task AC5_Fill_MultilanguageValues_UnicodePassedToPlaywright()
    {
        // Arrange — verify that Unicode characters are forwarded verbatim to Playwright
        string? capturedValue = null;
        _locatorMock
            .Setup(l => l.FillAsync(It.IsAny<string>(), It.IsAny<LocatorFillOptions?>()))
            .Callback<string, LocatorFillOptions?>((v, _) => capturedValue = v)
            .Returns(Task.CompletedTask);
        // AC#1 read-back: InputValueAsync returns the same Unicode string that was set
        _locatorMock
            .Setup(l => l.InputValueAsync(It.IsAny<LocatorInputValueOptions?>()))
            .ReturnsAsync("田中　太郎");

        var sut       = CreateExecutorWithLocator(_locatorMock.Object);
        const string japanese = "田中　太郎";   // Japanese full-width space + kanji
        var step = new BrowserStep
        {
            StepId   = "fill-jp-name",
            Action   = "browser.element.fill",
            Selector = new SelectorRef { Label = "名前" },
            Value    = japanese
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeTrue("Unicode fill must succeed");
        capturedValue.Should().Be(japanese,
            "AC#5: the exact multilingual string must be forwarded to Playwright unchanged");
        result.Evidence.Should().ContainKey("filledValue",
            "AC#1/AC#5: read-back must confirm the multilingual value was accepted by the field");
        result.Evidence["filledValue"].Should().Be(japanese,
            "AC#1/AC#5: the read-back value must match the declared Unicode fill value exactly");
    }

    // =========================================================================
    // AC#6 — Drag-and-drop (WEB-003)
    // =========================================================================

    [Fact]
    public async Task AC6_DragDrop_SupportedTargets_ProducesIntendedDragResult()
    {
        // Arrange — source and target are two distinct locator mocks
        var sourceMock = new Mock<ILocator>();
        var targetMock = new Mock<ILocator>();

        sourceMock
            .Setup(l => l.DragToAsync(It.IsAny<ILocator>(), It.IsAny<LocatorDragToOptions?>()))
            .Returns(Task.CompletedTask);

        var sut  = CreateExecutorWithTwoLocators(sourceMock.Object, targetMock.Object);
        var step = new BrowserStep
        {
            StepId         = "drag-card",
            Action         = "browser.element.dragdrop",
            Selector       = new SelectorRef { TestId = "card-task-1" },
            TargetSelector = new SelectorRef { TestId = "column-done" }
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeTrue(
            "AC#6: drag-and-drop between two actionable locators must succeed");
        sourceMock.Verify(
            l => l.DragToAsync(targetMock.Object, It.IsAny<LocatorDragToOptions?>()),
            Times.Once,
            "AC#6: DragToAsync must be called exactly once with the resolved target locator");
    }

    [Fact]
    public async Task AC6_DragDrop_UnsupportedTarget_ReturnsActionabilityTimeout()
    {
        // Arrange — DragToAsync raises TimeoutException when target is not actionable
        var sourceMock = new Mock<ILocator>();
        var targetMock = new Mock<ILocator>();

        sourceMock
            .Setup(l => l.DragToAsync(It.IsAny<ILocator>(), It.IsAny<LocatorDragToOptions?>()))
            .ThrowsAsync(new PlaywrightException(
                "locator.dragTo: Timeout 30000ms exceeded. " +
                "Target element is not visible or not reachable."));

        var sut  = CreateExecutorWithTwoLocators(sourceMock.Object, targetMock.Object);
        var step = new BrowserStep
        {
            StepId         = "drag-out-of-bounds",
            Action         = "browser.element.dragdrop",
            Selector       = new SelectorRef { TestId = "card-task-2" },
            TargetSelector = new SelectorRef { TestId = "column-archived" }
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.TIMEOUT,
            "AC#6: an unsupported drag target must produce errorCategory=TIMEOUT");
        result.DetailCode.Should().Be("browser.actionabilityTimeout",
            "AC#6: out-of-bounds or inaccessible drop target must report actionabilityTimeout");
    }

    [Fact]
    public async Task AC6_DragDrop_MissingSourceSelector_ReturnsFailure()
    {
        // Arrange — step with no Selector (source)
        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId         = "drag-no-source",
            Action         = "browser.element.dragdrop",
            // Selector intentionally absent
            TargetSelector = new SelectorRef { TestId = "column-done" }
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeFalse(
            "browser.element.dragdrop without a source Selector must fail");
    }

    [Fact]
    public async Task AC6_DragDrop_MissingTargetSelector_ReturnsFailure()
    {
        // Arrange — step with a source but no TargetSelector
        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId   = "drag-no-target",
            Action   = "browser.element.dragdrop",
            Selector = new SelectorRef { TestId = "card-task-3" }
            // TargetSelector intentionally absent
        };

        // Act
        var result = await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        result.Succeeded.Should().BeFalse(
            "browser.element.dragdrop without a TargetSelector must fail");
    }

    // =========================================================================
    // Timeout forwarding — per-step timeout forwarded to Playwright (BRE-001)
    // =========================================================================

    [Fact]
    public async Task TimeoutForwarded_Select_ToPlaywright()
    {
        // Arrange
        LocatorSelectOptionOptions? capturedOptions = null;
        _locatorMock
            .Setup(l => l.SelectOptionAsync(
                It.IsAny<string>(),
                It.IsAny<LocatorSelectOptionOptions?>()))
            .Callback<string, LocatorSelectOptionOptions?>((_, o) => capturedOptions = o)
            .ReturnsAsync(["US"]);

        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId   = "select-with-timeout",
            Action   = "browser.element.select",
            Selector = new SelectorRef { Label = "Country" },
            Value    = "US",
            Timeout  = TimeSpan.FromSeconds(7)
        };

        // Act
        await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Timeout.Should().BeApproximately(7_000f, 1f,
            "BRE-001: per-step declared timeout must be forwarded to SelectOptionAsync exactly");
    }

    [Fact]
    public async Task TimeoutForwarded_DblClick_ToPlaywright()
    {
        // Arrange
        LocatorDblClickOptions? capturedOptions = null;
        _locatorMock
            .Setup(l => l.DblClickAsync(It.IsAny<LocatorDblClickOptions?>()))
            .Callback<LocatorDblClickOptions?>(o => capturedOptions = o)
            .Returns(Task.CompletedTask);

        var sut  = CreateExecutorWithLocator(_locatorMock.Object);
        var step = new BrowserStep
        {
            StepId   = "dblclick-timeout",
            Action   = "browser.element.dblclick",
            Selector = new SelectorRef { TestId = "row-item" },
            Timeout  = TimeSpan.FromSeconds(3)
        };

        // Act
        await sut.ExecuteAsync(_pageMock.Object, step, default);

        // Assert
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Timeout.Should().BeApproximately(3_000f, 1f,
            "BRE-001: declared timeout must be forwarded to DblClickAsync");
    }
}
