using Microsoft.Playwright;
using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Locators;

/// <summary>
/// Default implementation of <see cref="ISelectorStrategy"/> that resolves DOM elements
/// using an accessibility-first priority chain as mandated by DOM-001, DOM-002, WEB-010,
/// and spec §9 (F-08 normative selector schema reference).
/// </summary>
/// <remarks>
/// <para><b>Priority order</b> (first non-null field that yields exactly one match wins):</para>
/// <list type="number">
///   <item>role + accessible name (<see cref="SelectorRef.Role"/> / <see cref="SelectorRef.RoleName"/>)</item>
///   <item>form label text (<see cref="SelectorRef.Label"/> / <see cref="SelectorRef.AriaLabel"/>)</item>
///   <item>placeholder text (<see cref="SelectorRef.Placeholder"/>)</item>
///   <item>test-ID attribute (<see cref="SelectorRef.TestId"/>)</item>
///   <item>visible text (<see cref="SelectorRef.Text"/>)</item>
///   <item>CSS selector (<see cref="SelectorRef.Css"/>) — only after semantic strategies exhausted;
///         must already be policy-approved (validated at artifact time)</item>
///   <item>XPath (<see cref="SelectorRef.XPath"/>) — pragmatic fallback, not in normative list</item>
/// </list>
/// <para>
/// <b>Ambiguity rejection (DOM-002):</b> if a candidate strategy matches more than one element,
/// <see cref="LocatorAmbiguityException"/> is thrown immediately.
/// The caller must never silently click the first of multiple matches.
/// </para>
/// <para>
/// <b>Not-found (DOM-001 / WEB-010):</b> if no strategy matches any element,
/// <see cref="LocatorNotFoundException"/> is thrown.
/// </para>
/// </remarks>
public sealed class LocatorResolver : ISelectorStrategy
{
    /// <inheritdoc/>
    public async Task<ILocator> ResolveAsync(IPage page, SelectorRef selectorRef, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selectorRef);

        var candidates = BuildCandidates(page, selectorRef);

        foreach (var (locator, strategyName) in candidates)
        {
            ct.ThrowIfCancellationRequested();

            int count;
            try
            {
                count = await locator.CountAsync();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A strategy-build error (e.g. invalid role name) must not silently swallow
                // the inner problem; surface it as INTERNAL so the caller can diagnose.
                throw new InvalidOperationException(
                    $"Locator strategy '{strategyName}' threw during element count: {ex.Message}", ex);
            }

            switch (count)
            {
                case 1:
                    return locator;

                case > 1:
                    throw new LocatorAmbiguityException(
                        $"Locator strategy '{strategyName}' matched {count} elements without scoped " +
                        "disambiguation. Provide a more specific locator or add an explicit frameChain scope.",
                        "browser.multipleLocatorMatches");

                // count == 0 → strategy produced no match; try next candidate.
            }
        }

        throw new LocatorNotFoundException(
            "No element matched any of the declared locator strategies within the current DOM scope. " +
            "Verify the element is present and that accessible labels or a testId fallback are declared.",
            "browser.locatorNotFound");
    }

    // ---------------------------------------------------------------------------
    // Candidate builder — yields (locator, strategyName) pairs in priority order.
    // Only non-null fields are emitted; callers need not pre-filter.
    // ---------------------------------------------------------------------------

    private static IEnumerable<(ILocator Locator, string Strategy)> BuildCandidates(
        IPage page, SelectorRef sel)
    {
        // Priority 1 — ARIA role + accessible name
        if (sel.Role is not null)
        {
            if (Enum.TryParse<AriaRole>(sel.Role, ignoreCase: true, out var ariaRole))
            {
                var opts = sel.RoleName is not null
                    ? new PageGetByRoleOptions { Name = sel.RoleName }
                    : null;
                yield return (page.GetByRole(ariaRole, opts), "role");
            }
            // Unknown role strings are skipped; the artifact validator should have caught them,
            // but a lenient skip here prevents a hard crash on future ARIA additions.
        }

        // Priority 2 — form label text (Label takes precedence; AriaLabel is backward-compat alias)
        string? labelText = sel.Label ?? sel.AriaLabel;
        if (labelText is not null)
            yield return (page.GetByLabel(labelText), "label");

        // Priority 3 — placeholder text
        if (sel.Placeholder is not null)
            yield return (page.GetByPlaceholder(sel.Placeholder), "placeholder");

        // Priority 4 — data-testid attribute (policy-allowlisted at deployment level)
        if (sel.TestId is not null)
            yield return (page.GetByTestId(sel.TestId), "testId");

        // Priority 5 — visible text (exact match; BRW-US-007 AC-4 allows text fallback)
        if (sel.Text is not null)
            yield return (page.GetByText(sel.Text, new PageGetByTextOptions { Exact = true }), "text");

        // Priority 6 — CSS selector (already policy-validated at artifact time; last semantic fallback)
        if (sel.Css is not null)
            yield return (page.Locator(sel.Css), "css");

        // XPath — pragmatic fallback, not in the normative priority list
        if (sel.XPath is not null)
            yield return (page.Locator($"xpath={sel.XPath}"), "xpath");
    }
}

/// <summary>
/// Optional factory abstraction for tests that need to inject frame-scoped locators.
/// Production callers may pass <see langword="null"/> (default).
/// </summary>
public interface IFrameLocatorFactory
{
    IFrameLocator ResolveFrame(IPage page, IReadOnlyList<SelectorRef> frameChain);
}
