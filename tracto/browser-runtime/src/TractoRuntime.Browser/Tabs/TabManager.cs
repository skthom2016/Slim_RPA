using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Tabs;

/// <summary>
/// Implements <see cref="ITabManager"/> for BRW-US-012 (WEB-004) — Tab and Popup Management.
///
/// <para><b>AC#1 — Expected popups:</b><br/>
/// Callers register expected popup declarations via <see cref="DeclareExpectedPopup"/> before
/// the step that triggers the popup.  When a new page event fires, this manager matches it
/// to the oldest pending declaration (URL filter optional).  The matched page is stored and
/// made available via <see cref="SwitchToPopupAsync"/> so the caller can switch to it and
/// continue automation.</para>
///
/// <para><b>AC#2 — Unexpected popups:</b><br/>
/// A new page that does not match any pending declaration is recorded as an unexpected popup
/// error.  The popup is NOT silently closed or accepted.  The error is surfaced on the next
/// call to <see cref="ConsumeUnexpectedPopupError"/> and must be treated as a run-level
/// failure by the caller (errorCategory=POLICY_DENIED, detailCode=browser.unexpectedPopup).</para>
/// </summary>
public sealed class TabManager(ILogger<TabManager> logger) : ITabManager
{
    // Ordered queue of popup declarations registered by the script before the trigger action.
    private readonly ConcurrentQueue<PopupDeclaration> _pendingDeclarations = new();

    // popupId → IPage (resolved when the new-page event fires and matches a declaration)
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IPage>> _resolvers =
        new(StringComparer.Ordinal);

    // Unexpected popup error: url + message (AC#2)
    private (string url, string message)? _unexpectedPopup;
    private readonly SemaphoreSlim _unexpectedLock = new(1, 1);

    private bool _installed;
    private bool _disposed;

    // ─────────────────────────────────────────────────────────────────────────
    // ITabManager — installation
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Install(IBrowserContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_installed)
            throw new InvalidOperationException(
                "TabManager.Install must be called exactly once per run context.");

        // Playwright fires IBrowserContext.Page once for each new page (tab/popup)
        // opened within the context, regardless of which page triggered it.
        context.Page += OnNewPage;
        _installed = true;

        logger.LogDebug("TabManager installed on browser context (BRW-US-012 WEB-004).");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ITabManager — declaration
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void DeclareExpectedPopup(PopupDeclaration declaration)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaration.PopupId);

        // Create the TaskCompletionSource first so SwitchToPopupAsync can await it
        // even before the popup arrives.
        var tcs = new TaskCompletionSource<IPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_resolvers.TryAdd(declaration.PopupId, tcs))
        {
            throw new InvalidOperationException(
                $"A popup declaration with PopupId='{declaration.PopupId}' is already registered.");
        }

        _pendingDeclarations.Enqueue(declaration);

        logger.LogDebug(
            "Expected popup declared: PopupId={PopupId}, UrlContains={UrlFilter}.",
            declaration.PopupId,
            declaration.ExpectedUrlContains ?? "(any)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ITabManager — switching
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IPage> SwitchToPopupAsync(string popupId, TimeSpan timeout, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(popupId);

        if (!_resolvers.TryGetValue(popupId, out var tcs))
        {
            throw new InvalidOperationException(
                $"No popup declaration found for PopupId='{popupId}'. " +
                "Call DeclareExpectedPopup before the step that opens the popup.");
        }

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var page = await tcs.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            logger.LogInformation(
                "Switched to popup PopupId={PopupId}, url={Url}.",
                popupId, page.Url);
            return page;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Timed out waiting {timeout.TotalSeconds:F0}s for popup PopupId='{popupId}' to appear.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ITabManager — unexpected popup error (AC#2)
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool HasUnexpectedPopup => _unexpectedPopup.HasValue;

    /// <inheritdoc/>
    public (string url, string message)? ConsumeUnexpectedPopupError()
    {
        _unexpectedLock.Wait();
        try
        {
            var err = _unexpectedPopup;
            _unexpectedPopup = null;
            return err;
        }
        finally
        {
            _unexpectedLock.Release();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IAsyncDisposable
    // ─────────────────────────────────────────────────────────────────────────

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;

        // Cancel any still-pending popup awaits so callers unblock with an error.
        foreach (var (popupId, tcs) in _resolvers)
        {
            tcs.TrySetCanceled();
            logger.LogDebug("Cancelled pending popup resolver: PopupId={PopupId}.", popupId);
        }

        _resolvers.Clear();
        _unexpectedLock.Dispose();
        return ValueTask.CompletedTask;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Event handler — fires on the thread pool when a new page opens
    // ─────────────────────────────────────────────────────────────────────────

    private void OnNewPage(object? sender, IPage page)
    {
        if (_disposed) return;

        var pageUrl = page.Url;

        // Walk the pending declarations in order; claim the first one whose URL filter matches.
        // AC#1 — expected popup: link it to the correct browser context.
        // We must pop from a queue but ConcurrentQueue doesn't let us look-ahead without
        // dequeuing, so we drain all declarations into a local list to find a match, then
        // re-enqueue the unmatched ones.

        PopupDeclaration? matched = null;
        var requeue = new List<PopupDeclaration>();

        while (_pendingDeclarations.TryDequeue(out var decl))
        {
            if (matched is null && IsUrlMatch(decl, pageUrl))
            {
                matched = decl;
            }
            else
            {
                requeue.Add(decl);
            }
        }

        // Restore unmatched declarations in order.
        foreach (var decl in requeue)
            _pendingDeclarations.Enqueue(decl);

        if (matched is not null)
        {
            // Resolve the awaiter registered by DeclareExpectedPopup.
            if (_resolvers.TryGetValue(matched.PopupId, out var tcs))
            {
                tcs.TrySetResult(page);
                logger.LogInformation(
                    "Expected popup linked: PopupId={PopupId}, url={Url}.",
                    matched.PopupId, pageUrl);
            }
            return;
        }

        // AC#2 — unexpected popup: record but do NOT silently close or accept.
        _unexpectedLock.Wait();
        try
        {
            _unexpectedPopup = (pageUrl,
                $"An undeclared popup was opened at '{pageUrl}'. " +
                "Declare the popup with browser.tab.declare before the action that opens it.");
        }
        finally
        {
            _unexpectedLock.Release();
        }

        logger.LogWarning(
            "Unexpected popup detected at url={Url}. " +
            "Automation must stop (BRW-US-012 AC#2).", pageUrl);
    }

    private static bool IsUrlMatch(PopupDeclaration decl, string pageUrl)
    {
        if (decl.ExpectedUrlContains is null) return true;
        return pageUrl.Contains(decl.ExpectedUrlContains, StringComparison.OrdinalIgnoreCase);
    }
}
