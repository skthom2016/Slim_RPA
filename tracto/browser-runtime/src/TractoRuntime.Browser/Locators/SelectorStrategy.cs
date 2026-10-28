using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Locators;

/// <summary>
/// Resolves a <see cref="SelectorRef"/> to a Playwright <see cref="ILocator"/> using the
/// priority ordering declared in spec §9 F-08:
///   Role → Label/AriaLabel → Placeholder → TestId → Text (exact) → Css → XPath
///
/// <para><b>Re-resolution semantics (DOM-006 / BRE-005 / BRW-US-009 AC#3):</b><br/>
/// This method creates a fresh locator <em>expression</em> on every call. Playwright
/// locators are lazy: the DOM is not queried until an action is dispatched. By calling
/// <see cref="ResolveAsync"/> immediately before each action, callers guarantee that
/// Playwright re-queries the live DOM even after asynchronous re-renders — eliminating
/// stale element handle errors without blind retries.</para>
///
/// <para><b>Strategy selection (DOM-001, DOM-002, WEB-010):</b><br/>
/// Semantic locators (Role, Label, Placeholder, TestId, Text) are preferred. CSS and XPath
/// are supported as fallbacks for sites without accessible names, subject to deployment
/// policy. <see cref="SelectorRef.Label"/> takes precedence over the backward-compatible
/// <see cref="SelectorRef.AriaLabel"/> alias; both resolve via Playwright
/// <c>GetByLabel</c>.</para>
/// </summary>
public sealed class SelectorStrategy(ILogger<SelectorStrategy> logger) : ISelectorStrategy
{
    public Task<ILocator> ResolveAsync(IPage page, SelectorRef selectorRef, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        string strategyUsed;
        ILocator locator;

        // Priority: semantic identifiers first, structural selectors last (spec §9 F-08)

        // Priority 1 — ARIA role + optional accessible name (highest semantic precision)
        if (selectorRef.Role is { } role &&
            Enum.TryParse<AriaRole>(role, ignoreCase: true, out var ariaRole))
        {
            var opts = selectorRef.RoleName is { } roleName
                ? new PageGetByRoleOptions { Name = roleName }
                : null;
            locator = page.GetByRole(ariaRole, opts);
            strategyUsed = "Role";
        }
        // Priority 2 — Label / AriaLabel (backward-compat alias): both resolve via GetByLabel
        else if ((selectorRef.Label ?? selectorRef.AriaLabel) is { } labelText)
        {
            locator = page.GetByLabel(labelText);
            strategyUsed = "Label";
        }
        // Priority 3 — input placeholder text
        else if (selectorRef.Placeholder is { } placeholder)
        {
            locator = page.GetByPlaceholder(placeholder);
            strategyUsed = "Placeholder";
        }
        // Priority 4 — data-testid attribute
        else if (selectorRef.TestId is { } testId)
        {
            locator = page.GetByTestId(testId);
            strategyUsed = "TestId";
        }
        // Priority 5 — visible text, exact match only — prevents accidental partial-text ambiguity (DOM-001)
        else if (selectorRef.Text is { } text)
        {
            locator = page.GetByText(text, new PageGetByTextOptions { Exact = true });
            strategyUsed = "Text(exact)";
        }
        // Priority 6 — CSS selector (already policy-validated at artifact time)
        else if (selectorRef.Css is { } css)
        {
            locator = page.Locator(css);
            strategyUsed = "Css";
        }
        // XPath — pragmatic fallback; use explicit xpath= prefix to prevent selector-engine ambiguity
        else if (selectorRef.XPath is { } xpath)
        {
            locator = page.Locator($"xpath={xpath}");
            strategyUsed = "XPath";
        }
        else
        {
            throw new InvalidOperationException(
                "SelectorRef must specify at least one of: Role, Label, AriaLabel, Placeholder, " +
                "TestId, Text, Css, XPath. All locator fields are null.");
        }

        logger.LogDebug("Locator expression created using strategy={Strategy}.", strategyUsed);
        return Task.FromResult(locator);
    }
}
