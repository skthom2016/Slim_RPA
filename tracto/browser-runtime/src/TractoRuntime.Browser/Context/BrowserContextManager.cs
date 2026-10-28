using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Network;
using TractoRuntime.Browser.Security;
using TractoRuntime.Browser.Session;

namespace TractoRuntime.Browser.Context;

/// <summary>
/// Implements <see cref="IBrowserContextManager"/> enforcing BRW-US-003 per-run isolation:
/// <list type="bullet">
///   <item>AC#1 — A fresh browser context (separate cookie store, cache, local storage) is created
///               for each new run by default; no state is inherited from a previous run.</item>
///   <item>AC#2 — Contexts are keyed by <c>runId</c>; concurrently leased workers therefore
///               cannot access each other's context.</item>
///   <item>AC#3 — Approved profile reuse requires an explicit signed policy reference registered
///               via <see cref="RegisterApprovedProfile"/> before <see cref="AcquireContextAsync"/>
///               is called.  Ad-hoc, in-script profile sharing is not permitted.</item>
///   <item>AC#4 — <see cref="ReleaseContextAsync"/> fully closes the run-owned context and releases
///               all its resources regardless of how the run terminates.</item>
/// </list>
///
/// BRW-US-005 additions:
/// <list type="bullet">
///   <item>When <see cref="BrowserContextManagerOptions.NetworkPolicy"/> is set, enterprise proxy
///         settings are applied to each new context (AC#5) and the
///         <see cref="INetworkPolicyEnforcer"/> is installed immediately after context creation
///         to enforce the origin allowlist via route interception (AC#1/AC#2).</item>
///   <item><c>IgnoreHTTPSErrors</c> is explicitly set to <c>false</c> on every context so
///         TLS certificate errors are never bypassed silently (BRW-008 / AC#6).</item>
/// </list>
/// </summary>
public sealed class BrowserContextManager(
    IBrowserFactory browserFactory,
    BrowserContextManagerOptions options,
    ISessionStore sessionStore,
    ILogger<BrowserContextManager> logger,
    INetworkPolicyEnforcer? networkPolicyEnforcer = null,
    ISecretResolver? secretResolver = null)
    : IBrowserContextManager
{
    // runId → active IBrowserContext (AC#2: keyed per run, never shared)
    private readonly ConcurrentDictionary<string, IBrowserContext> _contexts = new(StringComparer.Ordinal);

    // runId → signed storageState vault reference (AC#3: set before AcquireContextAsync)
    private readonly ConcurrentDictionary<string, string> _approvedProfileRefs = new(StringComparer.Ordinal);

    // Captured from primary constructor parameters to satisfy C# 14 CS9113 analysis
    // (primary constructor parameters used only in instance methods must be stored in fields)
    private readonly INetworkPolicyEnforcer? _networkPolicyEnforcer = networkPolicyEnforcer;
    private readonly ISecretResolver? _secretResolver = secretResolver;

    private IBrowser? _browser;
    private readonly SemaphoreSlim _browserLock = new(1, 1);
    private bool _disposed;

    // -------------------------------------------------------------------------
    // Approved profile registration — must be called before AcquireContextAsync
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers a signed vault reference for an approved <c>storageState</c> profile for a
    /// specific run.  This method MUST be called by the worker before
    /// <see cref="AcquireContextAsync"/> when the validated, signed artifact explicitly declares
    /// profile reuse.  An unregistered run always receives a completely fresh context (AC#3).
    /// </summary>
    /// <param name="runId">The run that is permitted to use the profile.</param>
    /// <param name="signedRef">Vault secret reference — never a raw token or JSON value.</param>
    public void RegisterApprovedProfile(string runId, string signedRef)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(signedRef);

        _approvedProfileRefs[runId] = signedRef;
        logger.LogDebug("Approved storageState profile reference registered for run {RunId}.", runId);
    }

    // -------------------------------------------------------------------------
    // IBrowserContextManager
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>
    /// AC#1: Context is created with null <c>StorageState</c> (no inherited cookies/storage)
    /// unless an approved signed reference was pre-registered via
    /// <see cref="RegisterApprovedProfile"/>.  The raw storageState value is resolved from the
    /// vault through <see cref="ISessionStore"/>; it never originates from user-supplied script
    /// content.
    /// </remarks>
    public async Task<IBrowserContext> AcquireContextAsync(
        string runId,
        BrowserMode mode,
        CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        if (_contexts.ContainsKey(runId))
            throw new InvalidOperationException(
                $"Run '{runId}' already has an active browser context. " +
                "Ensure ReleaseContextAsync was called for the previous attempt.");

        var browser = await EnsureBrowserAsync(mode, ct).ConfigureAwait(false);

        // AC#1 — always start fresh
        var contextOptions = await BuildContextOptionsAsync(ct).ConfigureAwait(false);

        // AC#3 — approved profile reuse: only when a signed reference was explicitly registered
        if (_approvedProfileRefs.TryGetValue(runId, out var signedRef))
        {
            var storageStateJson = await sessionStore
                .LoadStorageStateAsync(signedRef, ct)
                .ConfigureAwait(false);

            if (storageStateJson is not null)
            {
                // Value originates from the vault only — never from raw script/artifact content.
                // It must not be logged, traced, or placed in any Orchestrator payload.
                contextOptions.StorageState = storageStateJson;
                logger.LogDebug(
                    "Approved vault-sourced storageState applied for run {RunId}.", runId);
            }
        }

        var context = await browser
            .NewContextAsync(contextOptions)
            .ConfigureAwait(false);

        // BRW-US-005: Install origin policy enforcement immediately after context creation.
        // This ensures no request can escape enforcement before the first navigation (AC#1).
        // Guard: if InstallAsync or TryAdd fails, close the context to prevent a resource leak.
        try
        {
            if (_networkPolicyEnforcer is not null)
            {
                await _networkPolicyEnforcer.InstallAsync(context, ct).ConfigureAwait(false);
                logger.LogDebug("Network policy enforcer installed for run {RunId}.", runId);
            }

            // AC#2 — register under the run's own key; concurrent workers have distinct keys
            if (!_contexts.TryAdd(runId, context))
            {
                throw new InvalidOperationException(
                    $"Concurrent registration conflict for run '{runId}'.");
            }
        }
        catch
        {
            // Ensure the newly created context is closed on any failure so it is never leaked.
            try { await context.CloseAsync().ConfigureAwait(false); }
            catch (Exception closeEx)
            {
                logger.LogWarning(closeEx,
                    "Failed to close browser context during error cleanup for run {RunId}.", runId);
            }

            throw;
        }

        logger.LogInformation(
            "Isolated browser context acquired for run {RunId} (mode={Mode}).", runId, mode);

        return context;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// AC#4: The context is unconditionally closed and its resources released.  This method is
    /// safe to call on success, failure, cancellation, and timeout terminal states.
    /// </remarks>
    public async Task ReleaseContextAsync(string runId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        // AC#4 — remove from the tracking dictionary before closing to prevent double-close
        if (_contexts.TryRemove(runId, out var context))
        {
            try
            {
                await context.CloseAsync().ConfigureAwait(false);
                logger.LogInformation(
                    "Browser context released for run {RunId}; all resources freed.", runId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Error while closing browser context for run {RunId}; resources may be leaked.",
                    runId);
                throw;
            }
        }
        else
        {
            logger.LogDebug(
                "ReleaseContextAsync called for run {RunId} but no active context was found " +
                "(already released or never acquired).", runId);
        }

        // Always clear the profile reference so a subsequent run with the same ID starts fresh
        _approvedProfileRefs.TryRemove(runId, out _);
    }

    // -------------------------------------------------------------------------
    // IAsyncDisposable
    // -------------------------------------------------------------------------

    /// <summary>
    /// Closes all unreleased contexts and the underlying browser process.
    /// Ensures no orphaned Playwright processes remain (BRW-004 / AC#4).
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Close any contexts that were never explicitly released
        foreach (var (runId, context) in _contexts)
        {
            try
            {
                await context.CloseAsync().ConfigureAwait(false);
                logger.LogInformation(
                    "Disposed unreleased browser context for run {RunId} during manager shutdown.",
                    runId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Error disposing browser context for run {RunId} during shutdown.", runId);
            }
        }

        _contexts.Clear();
        _approvedProfileRefs.Clear();

        // Close the underlying browser process
        if (_browser is not null)
        {
            try
            {
                await _browser.CloseAsync().ConfigureAwait(false);
                logger.LogInformation("Browser process closed during manager disposal.");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error closing browser process during manager disposal.");
            }

            _browser = null;
        }

        _browserLock.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<IBrowser> EnsureBrowserAsync(BrowserMode mode, CancellationToken ct)
    {
        if (_browser is not null) return _browser;

        await _browserLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_browser is not null) return _browser;

            _browser = await browserFactory
                .LaunchAsync(mode, ct)
                .ConfigureAwait(false);

            logger.LogInformation("Browser launched (mode={Mode}).", mode);
            return _browser;
        }
        finally
        {
            _browserLock.Release();
        }
    }

    /// <summary>
    /// Builds fresh context options with no inherited state, certified viewport,
    /// explicit TLS-error-not-ignored flag, and optionally proxy settings resolved
    /// from the network policy (BRW-US-005 AC#5, AC#6).
    /// </summary>
    private async Task<BrowserNewContextOptions> BuildContextOptionsAsync(CancellationToken ct)
    {
        var contextOptions = new BrowserNewContextOptions
        {
            StorageState = null,                    // AC#1 — no inherited state by default
            IgnoreHTTPSErrors = false,              // AC#6 — TLS errors are never bypassed silently
            ViewportSize = new ViewportSize
            {
                Width = options.ViewportWidth,
                Height = options.ViewportHeight
            },
            DeviceScaleFactor = options.DeviceScaleFactor,
            Locale = options.Locale,
            TimezoneId = options.TimezoneId
        };

        // AC#5: Apply enterprise proxy settings when declared in the deployment policy.
        if (options.NetworkPolicy?.ProxyConfig is { } proxyCfg)
        {
            string? resolvedPassword = null;
            if (proxyCfg.PasswordSecretRef is not null && _secretResolver is not null)
            {
                // Resolve the proxy password from the vault at context-creation time.
                // The resolved value must never be logged, traced, or placed in payloads.
                resolvedPassword = await _secretResolver
                    .ResolveAsync(proxyCfg.PasswordSecretRef, ct)
                    .ConfigureAwait(false);
            }

            contextOptions.Proxy = new Proxy
            {
                Server   = proxyCfg.Server,
                Bypass   = proxyCfg.Bypass,
                Username = proxyCfg.Username,
                Password = resolvedPassword    // null when no auth required
            };

            logger.LogInformation(
                "Enterprise proxy configured for browser context: server='{Server}' (BRW-US-005 AC#5).",
                proxyCfg.Server);
        }

        return contextOptions;
    }
}
