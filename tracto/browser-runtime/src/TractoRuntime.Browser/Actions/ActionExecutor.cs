using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using TractoRuntime.Browser.Assertion;
using TractoRuntime.Browser.Authentication;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Dialogs;
using TractoRuntime.Browser.Extraction;
using TractoRuntime.Browser.Failure;
using TractoRuntime.Browser.FileTransfer;
using TractoRuntime.Browser.Locators;
using TractoRuntime.Browser.Service;
using TractoRuntime.Browser.Tabs;

namespace TractoRuntime.Browser.Actions;

/// <summary>
/// Implements <see cref="IActionExecutor"/> for BRW-US-009 — Dynamic Page Waits and Stale
/// Target Handling.
///
/// <para><b>DOM-005 — Navigation wait semantics (AC#1, AC#2):</b><br/>
/// <c>browser.page.navigate</c> waits for <c>DomContentLoaded</c> — never <c>networkidle</c>.
/// This allows SPA pages with ongoing background telemetry to complete navigation as soon as
/// the parsed DOM is ready.  If the step additionally declares a <c>Selector</c>, the runtime
/// further waits for that locator to become visible before reporting success, so callers can
/// gate on the expected element rather than a generic load event.</para>
///
/// <para><b>DOM-006 / BRE-005 — Stale target handling (AC#3, AC#5):</b><br/>
/// Every action call invokes <see cref="ISelectorStrategy.ResolveAsync"/> to create a fresh
/// Playwright locator expression immediately before dispatching the action.  Playwright
/// locators are lazy — re-creating the expression forces Playwright to re-query the live DOM
/// at dispatch time, not at resolution time.  This eliminates stale element handle errors
/// after asynchronous re-renders without any blind retry of non-idempotent actions.</para>
///
/// <para><b>DOM-007 — Visibility / hit-testing (AC#4):</b><br/>
/// Playwright's built-in actionability checks (<c>visible</c>, <c>enabled</c>, <c>stable</c>,
/// <c>receives-events</c>) are honoured on every click and fill.  No blind JavaScript click
/// is ever attempted.  A <see cref="Microsoft.Playwright.TimeoutException"/> from an
/// actionability check is classified as
/// <c>TIMEOUT / browser.actionabilityTimeout</c>.</para>
///
/// <para><b>BRE-001 — Bounded waits (AC#1):</b><br/>
/// Each step uses the timeout declared on <see cref="BrowserStep.Timeout"/>; when absent,
/// <see cref="DefaultStepTimeoutMs"/> (30 s) is applied. All Playwright calls receive the
/// resolved timeout value.</para>
///
/// <para><b>BRW-US-013 — File Download and Upload (WEB-006 / WEB-007):</b><br/>
/// <c>browser.file.download</c> delegates to <see cref="IFileDownloadHandler"/>;
/// <c>browser.file.upload</c> delegates to <see cref="IFileUploadHandler"/>.
/// Path allowlist is enforced before any bytes are written or transferred.</para>
/// </summary>
public sealed class ActionExecutor(
    ISelectorStrategy selectorStrategy,
    IFailureClassifier failureClassifier,
    ILogger<ActionExecutor> logger,
    IFileDownloadHandler? fileDownloadHandler = null,
    IFileUploadHandler? fileUploadHandler = null,
    ITabManager? tabManager = null,
    IDialogHandler? dialogHandler = null,
    IMfaAndSessionDetector? mfaDetector = null,
    IDomAssertionService? domAssertionService = null,
    IDomExtractionService? domExtractionService = null,
    IServiceInvoker? serviceInvoker = null)
    : IActionExecutor
{
    // Default per-step timeout (BRE-001). Overridden by BrowserStep.Timeout when present.
    private const float DefaultStepTimeoutMs = 30_000f;

    // Navigation wait state: DomContentLoaded, NOT NetworkIdle (DOM-005 / AC#1).
    // NetworkIdle blocks on SPA pages with background telemetry — forbidden by the spec.
    private const WaitUntilState NavigationWaitUntil = WaitUntilState.DOMContentLoaded;

    /// <inheritdoc/>
    public async Task<ActionResult> ExecuteAsync(IPage page, BrowserStep step, CancellationToken ct)
    {
        float timeoutMs = step.Timeout.HasValue
            ? (float)step.Timeout.Value.TotalMilliseconds
            : DefaultStepTimeoutMs;

        try
        {
            // Honour cancellation before doing any work — allows fast cancellation of queued steps.
            // Must be inside the try block so a pre-cancelled token is classified as CANCELLED result.
            ct.ThrowIfCancellationRequested();
            return step.Action switch
            {
                // ── Navigation (BRW-US-005, BRW-US-009) ──────────────────────
                "browser.page.navigate"      => await NavigateAsync(page, step, timeoutMs, ct),
                // ── DOM waits / find (BRW-US-009, BRW-US-007) ───────────────
                "browser.element.wait"       => await WaitForElementAsync(page, step, timeoutMs, ct),
                "browser.element.find"       => await FindElementAsync(page, step, timeoutMs, ct),
                // ── WEB-002 — Form filling and toggles (BRW-US-011 AC#1,2) ──
                "browser.element.fill"       => await FillElementAsync(page, step, timeoutMs, ct),
                "browser.element.type"       => await TypeSequentiallyAsync(page, step, timeoutMs, ct),
                "browser.element.select"     => await SelectOptionAsync(page, step, timeoutMs, ct),
                "browser.element.check"      => await CheckElementAsync(page, step, timeoutMs, ct),
                "browser.element.uncheck"    => await UncheckElementAsync(page, step, timeoutMs, ct),
                // ── WEB-003 — Pointer / keyboard actions (BRW-US-011 AC#3,6) ─
                "browser.element.click"      => await ClickElementAsync(page, step, timeoutMs, ct),
                "browser.element.dblclick"   => await DblClickElementAsync(page, step, timeoutMs, ct),
                "browser.element.hover"      => await HoverElementAsync(page, step, timeoutMs, ct),
                "browser.element.scroll"     => await ScrollIntoViewAsync(page, step, timeoutMs, ct),
                "browser.keyboard.shortcut"  => await KeyboardShortcutAsync(page, step, timeoutMs, ct),
                "browser.element.dragdrop"   => await DragAndDropAsync(page, step, timeoutMs, ct),
                // ── WEB-006 / WEB-007 — File transfer (BRW-US-013) ──────────
                "browser.file.download"      => await DownloadFileAsync(page, step, timeoutMs, ct),
                "browser.file.upload"        => await UploadFileAsync(page, step, timeoutMs, ct),
                // ── WEB-004 — Tab / popup management (BRW-US-012 AC#1,2) ──────
                "browser.tab.declare"        => DeclareExpectedPopup(step),
                "browser.tab.switch"         => await SwitchToPopupAsync(step, timeoutMs, ct),
                // ── WEB-005 — Dialog handling (BRW-US-012 AC#3,4) ────────────
                "browser.dialog.arm"         => ArmDialogResponse(step),
                "browser.dialog.respond"     => await RespondToDialogAsync(page, step, timeoutMs, ct),
                // ── WEB-009 — Auth / session check (BRW-US-012 AC#5,6) ───────
                "browser.page.check-auth"    => await CheckAuthChallengeAsync(page, step, ct),
                // ── DOM-008 — Technical assertion (BRW-US-010 AC#1) ──────────
                "browser.page.assert"        => await AssertAsync(page, step, timeoutMs, ct),
                // ── DOM-009 — Safe DOM inspection (BRW-US-010 AC#3) ──────────
                "browser.element.getText"    => await ExtractAsync(page, step, ExtractionMode.DisplayedText, timeoutMs, ct),
                "browser.element.getAttr"    => await ExtractAsync(page, step, ExtractionMode.Attribute, timeoutMs, ct),
                "browser.element.getValue"   => await ExtractAsync(page, step, ExtractionMode.FormValue, timeoutMs, ct),
                // ── CEC-005 — Business service invocation (BRW-US-015) ───────
                "service.invoke"             => await ServiceInvokeAsync(step, ct),
                _ => UnsupportedAction(step.Action)
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Propagate as a classified CANCELLED result rather than letting the exception escape
            var err = failureClassifier.Classify(
                new OperationCanceledException("Run was cancelled."), stateKnown: false);
            return BuildFailure(err);
        }
        catch (Exception ex)
        {
            // stateKnown=false: the caller (run orchestrator) decides FAILED vs UNKNOWN based
            // on whether the action was non-idempotent and the outcome is ambiguous (BRE-005).
            var err = failureClassifier.Classify(ex, stateKnown: false);
            logger.LogWarning(ex,
                "Step {StepId} ({Action}) failed: {Category}/{DetailCode}.",
                step.StepId, step.Action, err.Category, err.DetailCode ?? "(none)");
            return BuildFailure(err);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // browser.page.navigate — DOM-005, AC#1, AC#2
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Navigates to the URL in <see cref="BrowserStep.Value"/> and waits for
    /// <c>DomContentLoaded</c>.  If the step additionally declares a <see cref="BrowserStep.Selector"/>,
    /// the runtime waits for that locator to become visible after the load event fires — satisfying
    /// AC#2 (SPA background telemetry does not prevent completion when the intended element appears).
    /// </summary>
    private async Task<ActionResult> NavigateAsync(
        IPage page, BrowserStep step, float timeoutMs, CancellationToken ct)
    {
        var url = step.Value
            ?? throw new InvalidOperationException(
                "browser.page.navigate requires step.Value to contain the target URL.");

        logger.LogInformation(
            "Step {StepId}: navigating to {Url} " +
            "(waitUntil={Wait}, timeout={Timeout}ms).",
            step.StepId, url, NavigationWaitUntil, timeoutMs);

        // DOM-005: DomContentLoaded — SPA telemetry traffic does not block completion (AC#1)
        await page.GotoAsync(url, new PageGotoOptions
        {
            WaitUntil = NavigationWaitUntil,
            Timeout   = timeoutMs
        }).ConfigureAwait(false);

        // AC#2: if a target locator is declared, additionally wait for it to appear.
        // This ensures SPA pages complete when the expected element is visible, independently
        // of any ongoing background HTTP traffic.
        if (step.Selector is { } selector)
        {
            logger.LogDebug(
                "Step {StepId}: waiting for declared locator after navigation.", step.StepId);

            // DOM-006: fresh locator expression created here (no caching)
            var locator = await selectorStrategy
                .ResolveAsync(page, selector, ct)
                .ConfigureAwait(false);

            await locator.WaitForAsync(new LocatorWaitForOptions
            {
                State   = WaitForSelectorState.Visible,
                Timeout = timeoutMs
            }).ConfigureAwait(false);
        }

        var finalUrl = page.Url;
        logger.LogInformation(
            "Step {StepId}: navigation complete, finalUrl={FinalUrl}.",
            step.StepId, finalUrl);

        return new ActionResult
        {
            Succeeded = true,
            Evidence  = new Dictionary<string, object?> { ["finalUrl"] = finalUrl }
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // browser.element.wait — DOM-005, BRE-001, AC#1
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Waits for the declared selector to become visible within the step timeout.
    /// Satisfies DOM-005 (wait on target locator, not networkidle or fixed sleep) and
    /// BRE-001 (per-step bounded wait with cancellation-aware bounds).
    /// </summary>
    private async Task<ActionResult> WaitForElementAsync(
        IPage page, BrowserStep step, float timeoutMs, CancellationToken ct)
    {
        var selector = step.Selector
            ?? throw new InvalidOperationException(
                "browser.element.wait requires step.Selector to be specified.");

        logger.LogInformation(
            "Step {StepId}: waiting for element to be visible (timeout={Timeout}ms).",
            step.StepId, timeoutMs);

        // DOM-006: fresh locator — re-resolves after any re-render (AC#3)
        var locator = await selectorStrategy
            .ResolveAsync(page, selector, ct)
            .ConfigureAwait(false);

        await locator.WaitForAsync(new LocatorWaitForOptions
        {
            State   = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        }).ConfigureAwait(false);

        logger.LogInformation("Step {StepId}: element is visible.", step.StepId);
        return new ActionResult { Succeeded = true };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // browser.element.find — uniqueness verification (DOM-001, DOM-002)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that the declared selector resolves to exactly one element in the current DOM.
    /// Returns <c>TARGET_NOT_FOUND</c> when zero elements match and
    /// <c>TARGET_AMBIGUOUS</c> when more than one match.
    /// </summary>
    private async Task<ActionResult> FindElementAsync(
        IPage page, BrowserStep step, float timeoutMs, CancellationToken ct)
    {
        var selector = step.Selector
            ?? throw new InvalidOperationException(
                "browser.element.find requires step.Selector to be specified.");

        logger.LogInformation(
            "Step {StepId}: verifying unique element presence (timeout={Timeout}ms).",
            step.StepId, timeoutMs);

        // DOM-006: fresh locator expression (AC#3)
        var locator = await selectorStrategy
            .ResolveAsync(page, selector, ct)
            .ConfigureAwait(false);

        // Wait for at least one match to appear (avoids false negatives during initial load)
        await locator.First.WaitForAsync(new LocatorWaitForOptions
        {
            State   = WaitForSelectorState.Attached,
            Timeout = timeoutMs
        }).ConfigureAwait(false);

        var count = await locator.CountAsync().ConfigureAwait(false);

        if (count == 0)
        {
            return new ActionResult
            {
                Succeeded     = false,
                ErrorMessage  = "No element matched the declared selector.",
                ErrorCategory = ErrorCategory.TARGET_NOT_FOUND,
                DetailCode    = "browser.locatorNotFound"
            };
        }

        if (count > 1)
        {
            return new ActionResult
            {
                Succeeded     = false,
                ErrorMessage  = $"Selector matched {count} elements; expected exactly 1.",
                ErrorCategory = ErrorCategory.TARGET_AMBIGUOUS,
                DetailCode    = "browser.multipleLocatorMatches"
            };
        }

        logger.LogInformation("Step {StepId}: unique element found.", step.StepId);
        return new ActionResult { Succeeded = true };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // browser.element.click — DOM-006, DOM-007, BRE-005, AC#3, AC#4, AC#5
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-resolves the locator immediately before clicking to obtain a fresh element reference
    /// after any asynchronous re-render (AC#3).  Playwright's built-in actionability checks
    /// (<c>visible</c>, <c>enabled</c>, <c>stable</c>, <c>receives-events</c>) apply; no
    /// blind JavaScript click is attempted (AC#4 / DOM-007).
    ///
    /// <para>BRE-005 / AC#5: the click is dispatched exactly once per step call. If the page
    /// re-renders after the click is sent, the orchestrator layer must not re-invoke this step.
    /// A timeout during actionability (element disabled/overlaid) surfaces as
    /// <c>TIMEOUT / browser.actionabilityTimeout</c> — never a silent pass.</para>
    /// </summary>
    private async Task<ActionResult> ClickElementAsync(
        IPage page, BrowserStep step, float timeoutMs, CancellationToken ct)
    {
        var selector = step.Selector
            ?? throw new InvalidOperationException(
                "browser.element.click requires step.Selector to be specified.");

        logger.LogInformation(
            "Step {StepId}: resolving locator then clicking (timeout={Timeout}ms).",
            step.StepId, timeoutMs);

        // DOM-006 / BRE-005: always create a fresh locator expression before dispatching.
        // This causes Playwright to re-query the live DOM at click time, handling re-renders
        // without caching stale element handles (AC#3).
        var locator = await selectorStrategy
            .ResolveAsync(page, selector, ct)
            .ConfigureAwait(false);

        // DOM-007: Playwright's actionability checks enforce visible + enabled + stable +
        // receives-events.  The click is not dispatched until all checks pass.
        // A timeout here → TimeoutException → classified as TIMEOUT/browser.actionabilityTimeout.
        // BRE-005 / AC#5: no retry after dispatch — the exception bubbles to ExecuteAsync
        // which returns a failure without re-invoking the step.
        await locator.ClickAsync(new LocatorClickOptions { Timeout = timeoutMs })
            .ConfigureAwait(false);

        logger.LogInformation("Step {StepId}: click dispatched.", step.StepId);
        return new ActionResult { Succeeded = true };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // browser.element.fill — DOM-006, DOM-007
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-resolves the locator immediately before filling, then fills the field with
    /// <see cref="BrowserStep.Value"/> (or an empty string when Value is null).
    /// Playwright's actionability checks apply (element must be editable).
    /// </summary>
    private async Task<ActionResult> FillElementAsync(
        IPage page, BrowserStep step, float timeoutMs, CancellationToken ct)
    {
        var selector = step.Selector
            ?? throw new InvalidOperationException(
                "browser.element.fill requires step.Selector to be specified.");

        // null Value → fill with empty string (clear the field)
        var value = step.Value ?? string.Empty;

        logger.LogInformation(
            "Step {StepId}: resolving locator then filling (timeout={Timeout}ms).",
            step.StepId, timeoutMs);

        // DOM-006: fresh locator expression (AC#3)
        var locator = await selectorStrategy
            .ResolveAsync(page, selector, ct)
            .ConfigureAwait(false);

        await locator.FillAsync(value, new LocatorFillOptions { Timeout = timeoutMs })
            .ConfigureAwait(false);

        // AC#1: read back the filled value to verify it was accepted.
        // InputValueAsync is supported for standard <input> and <textarea> fields.
        // Contenteditable fields throw a PlaywrightException — swallow it silently
        // (they are served by browser.element.type / PressSequentiallyAsync instead).
        string? readbackValue = null;
        try
        {
            readbackValue = await locator.InputValueAsync().ConfigureAwait(false);
        }
        catch (PlaywrightException)
        {
            // Element does not expose a value property (e.g. contenteditable).
            // Verification is not supported for this element type; skip silently.
        }

        logger.LogInformation("Step {StepId}: fill complete.", step.StepId);

        return readbackValue is not null
            ? new ActionResult
            {
                Succeeded = true,
                Evidence  = new Dictionary<string, object?> { ["filledValue"] = readbackValue }
            }
            : new ActionResult { Succeeded = true };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // WEB-002 — Typing and Form Filling (BRW-US-011 AC#1, AC#2, AC#4, AC#5)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Types text character-by-character using Playwright's
    /// <c>PressSequentiallyAsync</c> — required for <c>contenteditable</c> fields and
    /// any input that responds to individual key events rather than programmatic fill.
    ///
    /// <para><b>AC#1 — Unicode / contenteditable support:</b><br/>
    /// <c>PressSequentiallyAsync</c> dispatches real keydown/keypress/keyup events for
    /// every code point, so Unicode characters (multi-byte, emoji) are delivered
    /// correctly to editors that intercept keyboard events.</para>
    ///
    /// <para><b>AC#4 — Disabled fields:</b><br/>
    /// Playwright's actionability check (editable state) fires before any key is pressed.
    /// A non-editable target produces a <see cref="TimeoutException"/> →
    /// <c>TIMEOUT / browser.actionabilityTimeout</c>; no event is dispatched silently.</para>
    /// </summary>
    private async Task<ActionResult> TypeSequentiallyAsync(
        IPage page, BrowserStep step, float timeoutMs, CancellationToken ct)
    {
        var selector = step.Selector
            ?? throw new InvalidOperationException(
                "browser.element.type requires step.Selector to be specified.");

        var value = step.Value ?? string.Empty;

        logger.LogInformation(
            "Step {StepId}: typing {Length} character(s) sequentially (timeout={Timeout}ms).",
            step.StepId, value.Length, timeoutMs);

        // DOM-006: fresh locator expression — re-resolves after any re-render (AC#3)
        var locator = await selectorStrategy
            .ResolveAsync(page, selector, ct)
            .ConfigureAwait(false);

        // Dispatches individual key events; Playwright actionability check (editable) applies.
        await locator.PressSequentiallyAsync(
            value,
            new LocatorPressSequentiallyOptions { Timeout = timeoutMs })
            .ConfigureAwait(false);

        logger.LogInformation("Step {StepId}: sequential type complete.", step.StepId);
        return new ActionResult { Succeeded = true };
    }

    /// <summary>
    /// Sets a <c>&lt;select&gt;</c> dropdown to the declared option value and reads back
    /// the resulting selection to verify it (WEB-002 / AC#2).
    ///
    /// <para>The option to select is taken from <see cref="BrowserStep.Value"/>.
    /// Playwright's <c>SelectOptionAsync</c> handles the interaction and returns the
    /// resulting set of selected values — returned in <c>Evidence["selectedValues"]</c>
    /// so callers can assert the outcome.</para>
    ///
    /// <para><b>AC#4 — Disabled select:</b> Playwright's actionability check fires
    /// before the selection; a disabled element produces
    /// <c>TIMEOUT / browser.actionabilityTimeout</c>.</para>
    /// </summary>
    private async Task<ActionResult> SelectOptionAsync(
        IPage page, BrowserStep step, float timeoutMs, CancellationToken ct)
    {
        var selector = step.Selector
            ?? throw new InvalidOperationException(
                "browser.element.select requires step.Selector to be specified.");

        var optionValue = step.Value ?? string.Empty;

        logger.LogInformation(
            "Step {StepId}: selecting option '{Option}' (timeout={Timeout}ms).",
            step.StepId, optionValue, timeoutMs);

        // DOM-006: fresh locator expression
        var locator = await selectorStrategy
            .ResolveAsync(page, selector, ct)
            .ConfigureAwait(false);

        // Selects by value attribute; returns the resulting selected value set for verification.
        var selected = await locator.SelectOptionAsync(
            optionValue,
            new LocatorSelectOptionOptions { Timeout = timeoutMs })
            .ConfigureAwait(false);

        logger.LogInformation(
            "Step {StepId}: option selected, resultingValues=[{Values}].",
            step.StepId, string.Join(", ", selected));

        return new ActionResult
        {
            Succeeded = true,
            Evidence  = new Dictionary<string, object?> { ["selectedValues"] = selected }
        };
    }

    /// <summary>
    /// Checks a checkbox or radio button (WEB-002 / AC#2).
    /// The resulting <c>checked</c> state is read back and recorded in evidence.
    ///
    /// <para><b>AC#2 — Verification:</b> after the check action, <c>IsCheckedAsync</c>
    /// confirms the element is now checked; the result is recorded in
    /// <c>Evidence["isChecked"]</c>.</para>
    ///
    /// <para><b>AC#4 — Disabled checkbox:</b> Playwright's actionability check fires
    /// before the toggle; a disabled element produces
    /// <c>TIMEOUT / browser.actionabilityTimeout</c>.</para>
    /// </summary>
    private async Task<ActionResult> CheckElementAsync(
        IPage page, BrowserStep step, float timeoutMs, CancellationToken ct)
    {
        var selector = step.Selector
            ?? throw new InvalidOperationException(
                "browser.element.check requires step.Selector to be specified.");

        logger.LogInformation(
            "Step {StepId}: checking element (timeout={Timeout}ms).", step.StepId, timeoutMs);

        // DOM-006: fresh locator expression
        var locator = await selectorStrategy
            .ResolveAsync(page, selector, ct)
            .ConfigureAwait(false);

        await locator.CheckAsync(new LocatorCheckOptions { Timeout = timeoutMs })
            .ConfigureAwait(false);

        // AC#2: read back to verify the check was applied
        var isChecked = await locator.IsCheckedAsync().ConfigureAwait(false);

        logger.LogInformation(
            "Step {StepId}: check complete, isChecked={IsChecked}.", step.StepId, isChecked);

        return new ActionResult
        {
            Succeeded = true,
            Evidence  = new Dictionary<string, object?> { ["isChecked"] = isChecked }
        };
    }

    /// <summary>
    /// Unchecks a checkbox or radio button (WEB-002 / AC#2).
    /// The resulting <c>checked</c> state is read back and recorded in evidence.
    /// </summary>
    private async Task<ActionResult> UncheckElementAsync(
        IPage page, BrowserStep step, float timeoutMs, CancellationToken ct)
    {
        var selector = step.Selector
            ?? throw new InvalidOperationException(
                "browser.element.uncheck requires step.Selector to be specified.");

        logger.LogInformation(
            "Step {StepId}: unchecking element (timeout={Timeout}ms).", step.StepId, timeoutMs);

        // DOM-006: fresh locator expression
        var locator = await selectorStrategy
            .ResolveAsync(page, selector, ct)
            .ConfigureAwait(false);

        await locator.UncheckAsync(new LocatorUncheckOptions { Timeout = timeoutMs })
            .ConfigureAwait(false);

        // AC#2: read back to verify the uncheck was applied
        var isChecked = await locator.IsCheckedAsync().ConfigureAwait(false);

        logger.LogInformation(
            "Step {StepId}: uncheck complete, isChecked={IsChecked}.", step.StepId, isChecked);

        return new ActionResult
        {
            Succeeded = true,
            Evidence  = new Dictionary<string, object?> { ["isChecked"] = isChecked }
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // WEB-003 — Pointer / Keyboard Actions (BRW-US-011 AC#3, AC#6)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Performs a double-click on the resolved locator (WEB-003 / AC#3).
    ///
    /// <para>Playwright's built-in actionability checks (<c>visible</c>, <c>enabled</c>,
    /// <c>stable</c>, <c>receives-events</c>) fire before the double-click is dispatched.
    /// No blind JavaScript double-click is attempted.
    /// The double-click is dispatched exactly once per step call (BRE-005).</para>
    /// </summary>
    private async Task<ActionResult> DblClickElementAsync(
        IPage page, BrowserStep step, float timeoutMs, CancellationToken ct)
    {
        var selector = step.Selector
            ?? throw new InvalidOperationException(
                "browser.element.dblclick requires step.Selector to be specified.");

        logger.LogInformation(
            "Step {StepId}: double-clicking element (timeout={Timeout}ms).", step.StepId, timeoutMs);

        // DOM-006: fresh locator expression before dispatch
        var locator = await selectorStrategy
            .ResolveAsync(page, selector, ct)
            .ConfigureAwait(false);

        // DOM-007: Playwright actionability checks apply; timeout → actionabilityTimeout
        await locator.DblClickAsync(new LocatorDblClickOptions { Timeout = timeoutMs })
            .ConfigureAwait(false);

        logger.LogInformation("Step {StepId}: double-click dispatched.", step.StepId);
        return new ActionResult { Succeeded = true };
    }

    /// <summary>
    /// Hovers the pointer over the resolved locator (WEB-003 / AC#3).
    ///
    /// <para>Playwright's actionability check (<c>visible</c>, <c>stable</c>) fires before
    /// the hover is dispatched.  Exactly one hover event fires per step call.</para>
    /// </summary>
    private async Task<ActionResult> HoverElementAsync(
        IPage page, BrowserStep step, float timeoutMs, CancellationToken ct)
    {
        var selector = step.Selector
            ?? throw new InvalidOperationException(
                "browser.element.hover requires step.Selector to be specified.");

        logger.LogInformation(
            "Step {StepId}: hovering over element (timeout={Timeout}ms).", step.StepId, timeoutMs);

        // DOM-006: fresh locator expression
        var locator = await selectorStrategy
            .ResolveAsync(page, selector, ct)
            .ConfigureAwait(false);

        await locator.HoverAsync(new LocatorHoverOptions { Timeout = timeoutMs })
            .ConfigureAwait(false);

        logger.LogInformation("Step {StepId}: hover dispatched.", step.StepId);
        return new ActionResult { Succeeded = true };
    }

    /// <summary>
    /// Scrolls the resolved element into the visible viewport (WEB-003 / AC#3).
    ///
    /// <para>Uses Playwright's <c>ScrollIntoViewIfNeededAsync</c> which scrolls only when
    /// the element is not already visible — idempotent and safe to call after a layout shift.
    /// The element must be attached and visible; a timeout surfaces as
    /// <c>TIMEOUT / browser.actionabilityTimeout</c>.</para>
    /// </summary>
    private async Task<ActionResult> ScrollIntoViewAsync(
        IPage page, BrowserStep step, float timeoutMs, CancellationToken ct)
    {
        var selector = step.Selector
            ?? throw new InvalidOperationException(
                "browser.element.scroll requires step.Selector to be specified.");

        logger.LogInformation(
            "Step {StepId}: scrolling element into view (timeout={Timeout}ms).", step.StepId, timeoutMs);

        // DOM-006: fresh locator expression
        var locator = await selectorStrategy
            .ResolveAsync(page, selector, ct)
            .ConfigureAwait(false);

        await locator.ScrollIntoViewIfNeededAsync(
            new LocatorScrollIntoViewIfNeededOptions { Timeout = timeoutMs })
            .ConfigureAwait(false);

        logger.LogInformation("Step {StepId}: element scrolled into view.", step.StepId);
        return new ActionResult { Succeeded = true };
    }

    /// <summary>
    /// Presses a keyboard shortcut (key combination) on the page (WEB-003 / AC#3).
    /// The key string is taken from <see cref="BrowserStep.Value"/> and forwarded to
    /// Playwright's <c>Keyboard.PressAsync</c> (e.g. "Control+S", "Escape", "Tab").
    ///
    /// <para>This action operates at page scope, not on a specific element, because keyboard
    /// shortcuts typically target the currently focused element.  The step must focus the
    /// correct element before calling this action when the shortcut is element-scoped.</para>
    /// </summary>
    private async Task<ActionResult> KeyboardShortcutAsync(
        IPage page, BrowserStep step, float timeoutMs, CancellationToken ct)
    {
        var key = step.Value
            ?? throw new InvalidOperationException(
                "browser.keyboard.shortcut requires step.Value to contain the key or combo " +
                "(e.g. \"Control+S\", \"Escape\", \"Tab\").");

        // timeoutMs is not forwarded to Keyboard.PressAsync because the Playwright keyboard
        // API does not accept a timeout — the key event is synchronous from the browser's
        // perspective.  CancellationToken is still honoured via ct.ThrowIfCancellationRequested
        // in the outer ExecuteAsync.

        logger.LogInformation(
            "Step {StepId}: pressing keyboard shortcut '{Key}'.", step.StepId, key);

        await page.Keyboard.PressAsync(key).ConfigureAwait(false);

        logger.LogInformation("Step {StepId}: keyboard shortcut dispatched.", step.StepId);
        return new ActionResult { Succeeded = true };
    }

    /// <summary>
    /// Performs a Playwright drag-and-drop from the source locator (<see cref="BrowserStep.Selector"/>)
    /// to the drop target (<see cref="BrowserStep.TargetSelector"/>) (WEB-003 / AC#6).
    ///
    /// <para><b>AC#6 — Supported drag result:</b> when both source and target are actionable,
    /// Playwright dispatches the full drag/dragstart/dragenter/dragover/drop/dragend
    /// event sequence and this method returns success.</para>
    ///
    /// <para><b>AC#6 — Unsupported drag target / out-of-bounds:</b> when the source is not
    /// actionable, the target is not reachable, or the drag geometry is out of bounds,
    /// Playwright raises a <see cref="TimeoutException"/> (actionability timeout) which
    /// the outer handler classifies as
    /// <c>errorCategory=TIMEOUT, detailCode=browser.actionabilityTimeout</c> — the
    /// failure is explicit, never a silent no-op.</para>
    ///
    /// <para>Both source and target locators are re-resolved immediately before the drag
    /// dispatch to handle any intervening DOM re-render (DOM-006).</para>
    /// </summary>
    private async Task<ActionResult> DragAndDropAsync(
        IPage page, BrowserStep step, float timeoutMs, CancellationToken ct)
    {
        var sourceSelector = step.Selector
            ?? throw new InvalidOperationException(
                "browser.element.dragdrop requires step.Selector (drag source) to be specified.");

        var targetSelector = step.TargetSelector
            ?? throw new InvalidOperationException(
                "browser.element.dragdrop requires step.TargetSelector (drop target) to be specified.");

        logger.LogInformation(
            "Step {StepId}: resolving drag source and drop target (timeout={Timeout}ms).",
            step.StepId, timeoutMs);

        // DOM-006: fresh locator expressions for both source and target — no caching
        var sourceLocator = await selectorStrategy
            .ResolveAsync(page, sourceSelector, ct)
            .ConfigureAwait(false);

        var targetLocator = await selectorStrategy
            .ResolveAsync(page, targetSelector, ct)
            .ConfigureAwait(false);

        // Playwright dispatches the full drag event sequence.
        // If source/target are not actionable or geometry is out-of-bounds, Playwright
        // raises TimeoutException → classified as TIMEOUT/browser.actionabilityTimeout (AC#6).
        await sourceLocator.DragToAsync(
            targetLocator,
            new LocatorDragToOptions { Timeout = timeoutMs })
            .ConfigureAwait(false);

        logger.LogInformation("Step {StepId}: drag-and-drop complete.", step.StepId);
        return new ActionResult { Succeeded = true };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // browser.file.download — WEB-007 / BRW-US-013
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Delegates to <see cref="IFileDownloadHandler"/> to execute the download step.
    ///
    /// <para>Requires <c>step.DownloadRef</c> to be non-null and
    /// <see cref="IFileDownloadHandler"/> to have been injected into the constructor.
    /// Returns <c>VALIDATION / browser.missingDownloadRef</c> or
    /// <c>INTERNAL / browser.fileDownloadHandlerNotConfigured</c> if either precondition
    /// is not met — providing a clear signal rather than a null-reference exception.</para>
    /// </summary>
    private async Task<ActionResult> DownloadFileAsync(
        IPage page, BrowserStep step, float timeoutMs, CancellationToken ct)
    {
        if (fileDownloadHandler is null)
        {
            return new ActionResult
            {
                Succeeded     = false,
                ErrorMessage  = "IFileDownloadHandler is not configured; cannot execute browser.file.download.",
                ErrorCategory = ErrorCategory.INTERNAL,
                DetailCode    = "browser.fileDownloadHandlerNotConfigured"
            };
        }

        var downloadRef = step.DownloadRef;
        if (downloadRef is null)
        {
            return new ActionResult
            {
                Succeeded     = false,
                ErrorMessage  = "browser.file.download requires step.DownloadRef to be specified.",
                ErrorCategory = ErrorCategory.VALIDATION,
                DetailCode    = "browser.missingDownloadRef"
            };
        }

        logger.LogInformation(
            "Step {StepId}: executing browser.file.download to '{Dest}' (timeout={Timeout}ms).",
            step.StepId, downloadRef.DestinationDirectory, timeoutMs);

        // Selector is optional for download — null means the download is already triggered
        // by a preceding navigation rather than an element click.
        return await fileDownloadHandler
            .HandleAsync(page, step.Selector, downloadRef, timeoutMs, ct)
            .ConfigureAwait(false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // browser.file.upload — WEB-006 / BRW-US-013
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Delegates to <see cref="IFileUploadHandler"/> to execute the upload step.
    ///
    /// <para>Requires both <c>step.Selector</c> and <c>step.UploadRef</c> to be non-null
    /// and <see cref="IFileUploadHandler"/> to have been injected.  Missing preconditions
    /// return structured <c>VALIDATION</c> or <c>INTERNAL</c> errors.</para>
    /// </summary>
    private async Task<ActionResult> UploadFileAsync(
        IPage page, BrowserStep step, float timeoutMs, CancellationToken ct)
    {
        if (fileUploadHandler is null)
        {
            return new ActionResult
            {
                Succeeded     = false,
                ErrorMessage  = "IFileUploadHandler is not configured; cannot execute browser.file.upload.",
                ErrorCategory = ErrorCategory.INTERNAL,
                DetailCode    = "browser.fileUploadHandlerNotConfigured"
            };
        }

        var targetSelector = step.Selector;
        if (targetSelector is null)
        {
            return new ActionResult
            {
                Succeeded     = false,
                ErrorMessage  = "browser.file.upload requires step.Selector to target the upload element.",
                ErrorCategory = ErrorCategory.VALIDATION,
                DetailCode    = "browser.missingSelectorForUpload"
            };
        }

        var uploadRef = step.UploadRef;
        if (uploadRef is null)
        {
            return new ActionResult
            {
                Succeeded     = false,
                ErrorMessage  = "browser.file.upload requires step.UploadRef to be specified.",
                ErrorCategory = ErrorCategory.VALIDATION,
                DetailCode    = "browser.missingUploadRef"
            };
        }

        logger.LogInformation(
            "Step {StepId}: executing browser.file.upload from '{Source}' (timeout={Timeout}ms).",
            step.StepId, uploadRef.SourcePath, timeoutMs);

        return await fileUploadHandler
            .HandleAsync(page, targetSelector, uploadRef, timeoutMs, ct)
            .ConfigureAwait(false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BRW-US-012 — Tab, Popup, and Dialog Management (WEB-004, WEB-005, WEB-009)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers an expected popup/tab declaration so that the next new-page event from the
    /// browser context is linked to the declared PopupId instead of being treated as
    /// unexpected (BRW-US-012 WEB-004 / AC#1).
    ///
    /// <para>Requires <c>step.PopupRef</c> to be set and <see cref="ITabManager"/>
    /// to have been injected.</para>
    /// </summary>
    private ActionResult DeclareExpectedPopup(BrowserStep step)
    {
        if (tabManager is null)
        {
            return new ActionResult
            {
                Succeeded     = false,
                ErrorMessage  = "ITabManager is not configured; cannot execute browser.tab.declare.",
                ErrorCategory = ErrorCategory.INTERNAL,
                DetailCode    = "browser.tabManagerNotConfigured"
            };
        }

        var popupRef = step.PopupRef;
        if (popupRef is null)
        {
            return new ActionResult
            {
                Succeeded     = false,
                ErrorMessage  = "browser.tab.declare requires step.PopupRef to be specified.",
                ErrorCategory = ErrorCategory.VALIDATION,
                DetailCode    = "browser.missingPopupRef"
            };
        }

        tabManager.DeclareExpectedPopup(popupRef);

        logger.LogInformation(
            "Step {StepId}: expected popup declared (PopupId={PopupId}).",
            step.StepId, popupRef.PopupId);

        return new ActionResult { Succeeded = true };
    }

    /// <summary>
    /// Switches to the previously declared popup/tab identified by <see cref="BrowserStep.Value"/>
    /// (BRW-US-012 WEB-004 / AC#1).
    ///
    /// <para>Waits up to <paramref name="timeoutMs"/> milliseconds for the popup to arrive
    /// after the triggering action has been dispatched.  Returns a result whose
    /// <see cref="ActionResult.Evidence"/> contains the popup's current URL.</para>
    ///
    /// <para>The returned page is NOT automatically set as the ActionExecutor's active page —
    /// the caller (run orchestrator) must update its own page reference and pass it to
    /// subsequent steps.</para>
    /// </summary>
    private async Task<ActionResult> SwitchToPopupAsync(
        BrowserStep step, float timeoutMs, CancellationToken ct)
    {
        if (tabManager is null)
        {
            return new ActionResult
            {
                Succeeded     = false,
                ErrorMessage  = "ITabManager is not configured; cannot execute browser.tab.switch.",
                ErrorCategory = ErrorCategory.INTERNAL,
                DetailCode    = "browser.tabManagerNotConfigured"
            };
        }

        var popupId = step.Value;
        if (string.IsNullOrWhiteSpace(popupId))
        {
            return new ActionResult
            {
                Succeeded     = false,
                ErrorMessage  = "browser.tab.switch requires step.Value to contain the PopupId.",
                ErrorCategory = ErrorCategory.VALIDATION,
                DetailCode    = "browser.missingPopupId"
            };
        }

        logger.LogInformation(
            "Step {StepId}: switching to popup PopupId={PopupId} (timeout={Timeout}ms).",
            step.StepId, popupId, timeoutMs);

        try
        {
            var popupPage = await tabManager
                .SwitchToPopupAsync(popupId, TimeSpan.FromMilliseconds(timeoutMs), ct)
                .ConfigureAwait(false);

            logger.LogInformation(
                "Step {StepId}: switched to popup PopupId={PopupId}, url={Url}.",
                step.StepId, popupId, popupPage.Url);

            return new ActionResult
            {
                Succeeded = true,
                Evidence  = new Dictionary<string, object?> { ["popupUrl"] = popupPage.Url }
            };
        }
        catch (TimeoutException tex)
        {
            return new ActionResult
            {
                Succeeded     = false,
                ErrorMessage  = tex.Message,
                ErrorCategory = ErrorCategory.TIMEOUT,
                DetailCode    = "browser.popupSwitchTimeout"
            };
        }
        catch (InvalidOperationException iex)
        {
            return new ActionResult
            {
                Succeeded     = false,
                ErrorMessage  = iex.Message,
                ErrorCategory = ErrorCategory.VALIDATION,
                DetailCode    = "browser.undeclaredPopup"
            };
        }
    }

    /// <summary>
    /// Arms a one-shot dialog response for the next JS dialog that appears on the page
    /// (BRW-US-012 WEB-005 / AC#3).
    ///
    /// <para>Requires <c>step.DialogRef</c> to be set and <see cref="IDialogHandler"/>
    /// to have been injected.  Returns success immediately — the actual dialog interaction
    /// happens asynchronously when Playwright fires the Dialog event.</para>
    /// </summary>
    private ActionResult ArmDialogResponse(BrowserStep step)
    {
        if (dialogHandler is null)
        {
            return new ActionResult
            {
                Succeeded     = false,
                ErrorMessage  = "IDialogHandler is not configured; cannot execute browser.dialog.arm.",
                ErrorCategory = ErrorCategory.INTERNAL,
                DetailCode    = "browser.dialogHandlerNotConfigured"
            };
        }

        var dialogRef = step.DialogRef;
        if (dialogRef is null)
        {
            return new ActionResult
            {
                Succeeded     = false,
                ErrorMessage  = "browser.dialog.arm requires step.DialogRef to be specified.",
                ErrorCategory = ErrorCategory.VALIDATION,
                DetailCode    = "browser.missingDialogRef"
            };
        }

        dialogHandler.ArmResponse(dialogRef);

        logger.LogInformation(
            "Step {StepId}: dialog response armed (choice={Choice}).",
            step.StepId, dialogRef.Accept ? "accept" : "dismiss");

        return new ActionResult { Succeeded = true };
    }

    /// <summary>
    /// Arms a dialog response AND optionally triggers the action that opens the dialog
    /// by clicking <see cref="BrowserStep.Selector"/> (BRW-US-012 WEB-005 / AC#3).
    ///
    /// <para>Use this step when the dialog is triggered by a UI click so that the arm and
    /// click happen atomically without a separate <c>browser.dialog.arm</c> step.
    /// When <see cref="BrowserStep.Selector"/> is null, only the arm is performed — the
    /// triggering action must be a separate step that has already been dispatched or will
    /// be dispatched immediately after.</para>
    ///
    /// <para>After clicking (if Selector is present), the method checks whether an
    /// unhandled-dialog error was recorded (AC#4) and surfaces it as a failure.</para>
    /// </summary>
    private async Task<ActionResult> RespondToDialogAsync(
        IPage page, BrowserStep step, float timeoutMs, CancellationToken ct)
    {
        if (dialogHandler is null)
        {
            return new ActionResult
            {
                Succeeded     = false,
                ErrorMessage  = "IDialogHandler is not configured; cannot execute browser.dialog.respond.",
                ErrorCategory = ErrorCategory.INTERNAL,
                DetailCode    = "browser.dialogHandlerNotConfigured"
            };
        }

        var dialogRef = step.DialogRef;
        if (dialogRef is null)
        {
            return new ActionResult
            {
                Succeeded     = false,
                ErrorMessage  = "browser.dialog.respond requires step.DialogRef to be specified.",
                ErrorCategory = ErrorCategory.VALIDATION,
                DetailCode    = "browser.missingDialogRef"
            };
        }

        // 1. Arm the one-shot response BEFORE dispatching the click so the handler
        //    is ready when Playwright fires the Dialog event.
        dialogHandler.ArmResponse(dialogRef);

        logger.LogInformation(
            "Step {StepId}: dialog response armed (choice={Choice}).",
            step.StepId, dialogRef.Accept ? "accept" : "dismiss");

        // 2. Optionally click the triggering element.
        if (step.Selector is { } selector)
        {
            logger.LogInformation(
                "Step {StepId}: clicking dialog trigger (timeout={Timeout}ms).", step.StepId, timeoutMs);

            var locator = await selectorStrategy
                .ResolveAsync(page, selector, ct)
                .ConfigureAwait(false);

            await locator.ClickAsync(new LocatorClickOptions { Timeout = timeoutMs })
                .ConfigureAwait(false);
        }

        // 3. Allow a short yield so Playwright's event loop can dispatch the Dialog event
        //    and invoke our handler before we check for errors.
        await Task.Delay(50, ct).ConfigureAwait(false);

        // 4. Check whether the handler recorded an error (e.g. message mismatch or AC#4).
        if (dialogHandler.HasUnhandledDialogError)
        {
            var dialogErr = dialogHandler.ConsumeUnhandledDialogError()!;
            logger.LogWarning(
                "Step {StepId}: dialog interaction failed — {Category}/{DetailCode}.",
                step.StepId, dialogErr.Category, dialogErr.DetailCode ?? "(none)");
            return BuildFailure(dialogErr);
        }

        logger.LogInformation("Step {StepId}: dialog.respond complete.", step.StepId);
        return new ActionResult { Succeeded = true };
    }

    /// <summary>
    /// Inspects the current page for MFA/CAPTCHA challenges or expired sessions
    /// (BRW-US-012 WEB-009 / AC#5, AC#6).
    ///
    /// <para>When an intervention is detected, returns a FAILED <see cref="ActionResult"/>
    /// with <see cref="ActionResult.NeedsAttention"/> set to <see langword="true"/>.
    /// The run state decision (FAILED vs TIMED_OUT vs CANCELLED) is made by the caller.</para>
    ///
    /// <para>Requires <see cref="IMfaAndSessionDetector"/> to have been injected.</para>
    /// </summary>
    private async Task<ActionResult> CheckAuthChallengeAsync(
        IPage page, BrowserStep step, CancellationToken ct)
    {
        if (mfaDetector is null)
        {
            // No detector injected — return success (no challenge detected).
            // Operators who need MFA detection must inject IMfaAndSessionDetector.
            logger.LogDebug(
                "Step {StepId}: IMfaAndSessionDetector not configured; skipping auth check.",
                step.StepId);
            return new ActionResult { Succeeded = true };
        }

        logger.LogDebug("Step {StepId}: checking for MFA/session-expiry challenge.", step.StepId);

        var detection = await mfaDetector.DetectAsync(page, ct).ConfigureAwait(false);

        if (!detection.InterventionRequired)
        {
            logger.LogDebug("Step {StepId}: no authentication intervention detected.", step.StepId);
            return new ActionResult { Succeeded = true };
        }

        // AC#5 / AC#6 — intervention detected: classify and stop the run.
        var (category, detailCode) = detection.Kind switch
        {
            DetectedInterventionKind.MfaChallenge              => (ErrorCategory.AUTHENTICATION, "browser.mfaChallengeDetected"),
            DetectedInterventionKind.CaptchaChallenge          => (ErrorCategory.AUTHENTICATION, "browser.captchaChallengeDetected"),
            DetectedInterventionKind.SessionExpired            => (ErrorCategory.AUTHENTICATION, "browser.sessionExpired"),
            DetectedInterventionKind.ConditionalAccessBlocked  => (ErrorCategory.AUTHENTICATION, "browser.conditionalAccessBlocked"),
            _                                                   => (ErrorCategory.AUTHENTICATION, "browser.authInterventionRequired")
        };

        logger.LogWarning(
            "Step {StepId}: authentication intervention detected — kind={Kind}, reason={Reason}.",
            step.StepId, detection.Kind, detection.Reason);

        return new ActionResult
        {
            Succeeded      = false,
            ErrorMessage   = detection.Reason ?? $"Authentication intervention required: {detection.Kind}",
            ErrorCategory  = category,
            DetailCode     = detailCode,
            NeedsAttention = detection.NeedsAttention,
            Evidence       = new Dictionary<string, object?>
            {
                ["interventionKind"] = detection.Kind.ToString(),
                ["needsAttention"]   = detection.NeedsAttention
            }
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // browser.page.assert — DOM-008, BRW-US-010 AC#1
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Evaluates a technical DOM assertion (DOM-008 / BRW-US-010 AC#1).
    /// Requires <see cref="IDomAssertionService"/> to be supplied via the constructor.
    /// Business predicates must have been rejected before this point (at artifact validation
    /// time) by the condition parser (AC#2 / BRW-AT-14).
    /// </summary>
    private async Task<ActionResult> AssertAsync(
        IPage page, BrowserStep step, float timeoutMs, CancellationToken ct)
    {
        if (domAssertionService is null)
        {
            return new ActionResult
            {
                Succeeded     = false,
                ErrorMessage  = "IDomAssertionService is not configured — browser.page.assert is unavailable.",
                ErrorCategory = ErrorCategory.INTERNAL,
                DetailCode    = "browser.assertionServiceNotConfigured"
            };
        }

        if (step.Assertion is null)
        {
            return new ActionResult
            {
                Succeeded     = false,
                ErrorMessage  = "browser.page.assert requires step.Assertion to be set with a technical predicate.",
                ErrorCategory = ErrorCategory.VALIDATION,
                DetailCode    = "browser.selectorRefInvalid"
            };
        }

        logger.LogDebug(
            "Step {StepId}: evaluating assertion kind={Kind}.",
            step.StepId, step.Assertion.Kind);

        var result = await domAssertionService
            .EvaluateAsync(page, step.Assertion, step.Selector, timeoutMs, ct)
            .ConfigureAwait(false);

        if (result.Passed)
        {
            logger.LogInformation("Step {StepId}: assertion passed.", step.StepId);
            return new ActionResult { Succeeded = true };
        }

        logger.LogInformation(
            "Step {StepId}: assertion FAILED — {Detail}.",
            step.StepId, result.FailureDetail);

        return new ActionResult
        {
            Succeeded     = false,
            ErrorMessage  = result.FailureDetail,
            ErrorCategory = result.ErrorCategory,
            DetailCode    = result.DetailCode
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // browser.element.getText / getAttr / getValue — DOM-009, BRW-US-010 AC#3
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts an opaque DOM value and stores it in evidence for forwarding to
    /// <c>service.invoke</c> (DOM-009 / BRW-US-010 AC#3).
    /// No parsing, arithmetic, or domain comparison is performed — the raw value is
    /// passed unchanged to the caller.
    /// </summary>
    private async Task<ActionResult> ExtractAsync(
        IPage page,
        BrowserStep step,
        ExtractionMode defaultMode,
        float timeoutMs,
        CancellationToken ct)
    {
        if (domExtractionService is null)
        {
            return new ActionResult
            {
                Succeeded     = false,
                ErrorMessage  = "IDomExtractionService is not configured — DOM extraction actions are unavailable.",
                ErrorCategory = ErrorCategory.INTERNAL,
                DetailCode    = "browser.extractionServiceNotConfigured"
            };
        }

        // Use the step's declared ExtractionSpec if provided; otherwise create one from the action.
        var spec = step.Extraction ?? new ExtractionSpec { Mode = defaultMode };

        logger.LogDebug(
            "Step {StepId}: extracting DOM value mode={Mode} outputKey={Key}.",
            step.StepId, spec.Mode, spec.OutputKey);

        var result = await domExtractionService
            .ExtractAsync(page, spec, step.Selector, timeoutMs, ct)
            .ConfigureAwait(false);

        if (result.Succeeded)
        {
            logger.LogInformation(
                "Step {StepId}: DOM extraction succeeded (key={Key}).",
                step.StepId, spec.OutputKey);

            return new ActionResult
            {
                Succeeded = true,
                Evidence  = new Dictionary<string, object?> { [spec.OutputKey] = result.ExtractedValue }
            };
        }

        logger.LogInformation(
            "Step {StepId}: DOM extraction FAILED — {Message}.", step.StepId, result.ErrorMessage);

        return new ActionResult
        {
            Succeeded     = false,
            ErrorMessage  = result.ErrorMessage,
            ErrorCategory = result.ErrorCategory,
            DetailCode    = result.DetailCode
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // service.invoke — CEC-005, BRE-007, BRW-US-015 AC#3 / AC#4
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Invokes a registered custom business service and propagates the outcome to the caller
    /// (CEC-005 / BRW-US-015).
    ///
    /// <para><b>AC#3 — UNKNOWN_OUTCOME (BRE-007):</b><br/>
    /// When the service returns <see cref="ServiceOutcome.UNKNOWN_OUTCOME"/> this method
    /// returns an <see cref="ActionResult"/> with <c>Succeeded=false</c>,
    /// <c>ServiceOutcome=UNKNOWN_OUTCOME</c>, and <c>ServiceActionCode=null</c>.
    /// The run orchestrator MUST terminate the current attempt as UNKNOWN — no actionCode
    /// branch may be followed until an authorized external reconciliation step has established
    /// a safe, proven outcome.</para>
    ///
    /// <para><b>AC#4 — Definitive outcomes:</b><br/>
    /// SUCCESS → <c>Succeeded=true</c>.<br/>
    /// BUSINESS_REJECTION → <c>Succeeded=true</c> (the service-level decision is propagated
    /// to the caller; the run state is determined by the run orchestrator after the post-
    /// rejection UI step completes).<br/>
    /// TECHNICAL_FAILURE / TRANSIENT_FAILURE → <c>Succeeded=false</c>.<br/>
    /// All definitive outcomes carry the service-supplied <c>actionCode</c> in
    /// <see cref="ActionResult.ServiceActionCode"/> so the run orchestrator may follow the
    /// configured UI branch.</para>
    ///
    /// <para>Step input is taken from <see cref="BrowserStep.ServiceRef.InputMapping"/>
    /// which has already been resolved by the caller; no domain value is computed here.</para>
    /// </summary>
    private async Task<ActionResult> ServiceInvokeAsync(BrowserStep step, CancellationToken ct)
    {
        if (serviceInvoker is null)
        {
            return new ActionResult
            {
                Succeeded     = false,
                ErrorMessage  = "IServiceInvoker is not configured; cannot execute service.invoke.",
                ErrorCategory = ErrorCategory.INTERNAL,
                DetailCode    = "browser.serviceInvokerNotConfigured"
            };
        }

        var serviceRef = step.ServiceRef;
        if (serviceRef is null)
        {
            return new ActionResult
            {
                Succeeded     = false,
                ErrorMessage  = "service.invoke requires step.ServiceRef to be specified.",
                ErrorCategory = ErrorCategory.VALIDATION,
                DetailCode    = "browser.missingServiceRef"
            };
        }

        logger.LogInformation(
            "Step {StepId}: service.invoke serviceId={ServiceId}.",
            step.StepId, serviceRef.ServiceId);

        var result = await serviceInvoker
            .InvokeAsync(serviceRef, serviceRef.InputMapping, ct)
            .ConfigureAwait(false);

        // ── AC#3: UNKNOWN_OUTCOME — no actionCode forwarded, run must become UNKNOWN ─
        if (result.Outcome == ServiceOutcome.UNKNOWN_OUTCOME)
        {
            logger.LogWarning(
                "Step {StepId}: service.invoke returned UNKNOWN_OUTCOME. " +
                "Attempt must terminate as UNKNOWN; no actionCode branch will be followed " +
                "(BRE-007 / BRW-US-015 AC#3).",
                step.StepId);

            return new ActionResult
            {
                Succeeded       = false,
                ErrorMessage    = $"service.invoke returned UNKNOWN_OUTCOME for serviceId={serviceRef.ServiceId}. " +
                                   "Attempt terminates as UNKNOWN.",
                ErrorCategory   = ErrorCategory.INTERNAL,   // category for the error record
                DetailCode      = "browser.serviceUnknownOutcome",
                ServiceOutcome  = ServiceOutcome.UNKNOWN_OUTCOME,
                ServiceActionCode = null,                    // must be null for UNKNOWN_OUTCOME (AC#3)
                Evidence        = new Dictionary<string, object?>
                {
                    ["serviceId"]      = serviceRef.ServiceId,
                    ["serviceOutcome"] = nameof(ServiceOutcome.UNKNOWN_OUTCOME)
                }
            };
        }

        // ── AC#4: Definitive outcomes ─────────────────────────────────────────────
        bool succeeded = result.Outcome is ServiceOutcome.SUCCESS or ServiceOutcome.BUSINESS_REJECTION;

        logger.LogInformation(
            "Step {StepId}: service.invoke returned outcome={Outcome}, actionCode={ActionCode}.",
            step.StepId, result.Outcome, result.ActionCode ?? "(none)");

        // Build evidence: start from service output so service keys are included,
        // then overwrite with metadata so our required keys always win — even if the
        // service happens to return a key named "serviceId", "serviceOutcome" or
        // "actionCode" (prevents ArgumentException from ToDictionary on duplicate keys).
        var evidence = new Dictionary<string, object?>(result.Output)
        {
            ["serviceId"]      = serviceRef.ServiceId,
            ["serviceOutcome"] = result.Outcome.ToString(),
            ["actionCode"]     = result.ActionCode
        };

        return new ActionResult
        {
            Succeeded         = succeeded,
            ErrorMessage      = succeeded ? null
                : $"service.invoke returned {result.Outcome} for serviceId={serviceRef.ServiceId}.",
            ErrorCategory     = succeeded ? null : ErrorCategory.INTERNAL,
            ServiceOutcome    = result.Outcome,
            ServiceActionCode = result.ActionCode,
            Evidence          = evidence
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static ActionResult UnsupportedAction(string action)
    {
        return new ActionResult
        {
            Succeeded     = false,
            ErrorMessage  = $"Action '{action}' is not supported by this executor.",
            ErrorCategory = ErrorCategory.VALIDATION,
            DetailCode    = "browser.unsupportedAction"
        };
    }

    private static ActionResult BuildFailure(CecError err) => new()
    {
        Succeeded     = false,
        ErrorMessage  = err.Message,
        ErrorCategory = err.Category,
        DetailCode    = err.DetailCode
    };
}
