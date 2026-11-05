using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using TractoRuntime.Browser.Context;
using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Host;

/// <summary>
/// Opens a browser on a named profile folder that survives between runs, so a site the client
/// signed into once stays signed in.
///
/// <para>This is the opt-in counterpart to <see cref="BrowserContextManager"/>, which gives every
/// run a throwaway context with nothing signed in. The two never mix: a run uses one or the
/// other, chosen by whether the script names a profile.</para>
///
/// <para>Playwright drives a persistent profile with
/// <c>LaunchPersistentContextAsync</c>, which both starts the browser and hands back its one
/// context; there is no separate browser object to close.</para>
/// </summary>
public sealed class PersistentProfileContextManager(
    IPlaywright playwright,
    string profileName,
    string profileDirectory,
    BrowserContextManagerOptions options,
    string? channel,
    ILogger<PersistentProfileContextManager> logger)
    : IBrowserContextManager
{
    private readonly string? _channel = string.IsNullOrWhiteSpace(channel) ? null : channel;
    private IBrowserContext? _context;
    private bool _disposed;

    /// <summary>
    /// Not used here. A persistent profile carries its own cookies on disk, so there is no
    /// separate stored session to approve.
    /// </summary>
    public void RegisterApprovedProfile(string runId, string signedRef) { }

    public async Task<IBrowserContext> AcquireContextAsync(string runId, BrowserMode mode, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        if (_context is not null)
            throw new InvalidOperationException(
                $"Profile '{profileName}' is already open for another run in this worker.");

        Directory.CreateDirectory(profileDirectory);

        var launch = new BrowserTypeLaunchPersistentContextOptions
        {
            Headless          = mode == BrowserMode.Headless,
            Channel           = _channel,
            IgnoreHTTPSErrors = false,
            ViewportSize      = new ViewportSize { Width = options.ViewportWidth, Height = options.ViewportHeight },
            Locale            = options.Locale,
            TimezoneId        = options.TimezoneId
        };

        try
        {
            _context = await playwright.Chromium
                .LaunchPersistentContextAsync(profileDirectory, launch)
                .ConfigureAwait(false);
        }
        catch (PlaywrightException ex)
        {
            // The browser keeps an exclusive lock on a profile folder while it is open.
            throw new ScriptValidationException(
                $"The browser could not open profile '{profileName}'. It is probably still open " +
                $"from another run or a sign-in window. Close it and try again. " +
                $"Folder: {profileDirectory}. Browser said: {ex.Message}");
        }

        logger.LogInformation(
            "Browser context opened on profile {Profile} for run {RunId} (mode={Mode}); " +
            "cookies from earlier runs are kept.", profileName, runId, mode);

        return _context;
    }

    /// <summary>
    /// Closes the context, which is what writes the profile's cookies to disk. The folder is
    /// kept, so the next run starts signed in.
    /// </summary>
    public async Task ReleaseContextAsync(string runId, CancellationToken ct)
    {
        if (_context is null) return;

        try
        {
            await _context.CloseAsync().ConfigureAwait(false);
            logger.LogInformation("Profile {Profile} closed and saved after run {RunId}.", profileName, runId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error closing profile {Profile} after run {RunId}.", profileName, runId);
        }
        finally
        {
            _context = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await ReleaseContextAsync("dispose", CancellationToken.None).ConfigureAwait(false);
    }
}
