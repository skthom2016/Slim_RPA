using Microsoft.Playwright;
using SysEnvironment = global::System.Environment;

namespace TractoRuntime.Browser.Binary;

/// <summary>
/// Resolves the on-disk path of a Playwright browser executable for the given browser type.
///
/// In connected (online) mode the browser is installed by <c>playwright install</c> and
/// lives in the Playwright home directory.  In isolated-host mode the same binary is
/// pre-bundled inside the offline package; no network access is performed.
///
/// The locator does NOT install missing binaries — <see cref="IBinaryValidator"/> is
/// responsible for rejecting an absent binary with the correct CEC error.
/// </summary>
public static class PlaywrightBinaryLocator
{
    /// <summary>
    /// Attempts to resolve the executable path for <paramref name="browserType"/>.
    /// Returns <c>null</c> when Playwright cannot find an installation for that browser type,
    /// so the caller can forward the <c>null</c> to <see cref="IBinaryValidator"/> and let
    /// it produce the correct CEC error.
    /// </summary>
    public static string? TryResolve(string browserType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(browserType);

        try
        {
            // Playwright.NET exposes an internal helper for resolving installed browser paths.
            // We use the standard Playwright home environment variable as the primary lookup.
            string playwrightHome = GetPlaywrightHome();

            // The Playwright package name → sub-directory mapping mirrors the Playwright CLI.
            string? subdirectory = browserType.ToLowerInvariant() switch
            {
                "chromium" => FindLatestRevisionSubdir(playwrightHome, "chromium"),
                "firefox"  => FindLatestRevisionSubdir(playwrightHome, "firefox"),
                "webkit"   => FindLatestRevisionSubdir(playwrightHome, "webkit"),
                _          => FindLatestRevisionSubdir(playwrightHome, browserType)
            };

            if (subdirectory is null)
                return null;

            string executableName = GetExecutableName(browserType);
            string candidate = Path.Combine(subdirectory, executableName);

            return File.Exists(candidate) ? candidate : null;
        }
        catch (Exception)
        {
            // If Playwright home discovery fails we return null and let IBinaryValidator
            // produce the canonical ENVIRONMENT / browser.notInstalled error.
            return null;
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the Playwright browser cache root, respecting the
    /// <c>PLAYWRIGHT_BROWSERS_PATH</c> environment variable override.
    /// </summary>
    private static string GetPlaywrightHome()
    {
        string? envOverride = System.Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH");
        if (!string.IsNullOrWhiteSpace(envOverride))
            return envOverride;

        // Default Playwright home on Windows: %USERPROFILE%\AppData\Local\ms-playwright
        string userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, "AppData", "Local", "ms-playwright");
    }

    /// <summary>
    /// Finds the highest-numbered revision sub-directory for the given browser prefix.
    /// Returns <c>null</c> if no matching directory exists.
    /// </summary>
    private static string? FindLatestRevisionSubdir(string home, string browserPrefix)
    {
        if (!Directory.Exists(home))
            return null;

        // Directory names follow the pattern "<browser>-<revision>" (e.g. "chromium-1148").
        string[] candidates = Directory.GetDirectories(home, $"{browserPrefix}-*");

        if (candidates.Length == 0)
            return null;

        // Pick the highest-numbered revision.
        Array.Sort(candidates, StringComparer.OrdinalIgnoreCase);
        return candidates[^1];
    }

    private static string GetExecutableName(string browserType) =>
        browserType.ToLowerInvariant() switch
        {
            "chromium" => OperatingSystem.IsWindows() ? Path.Combine("chrome-win", "chrome.exe") : "chrome",
            "firefox"  => OperatingSystem.IsWindows() ? Path.Combine("firefox", "firefox.exe") : "firefox",
            // Playwright WebKit on Windows: executable is Playwright.exe directly in the
            // webkit-<revision> directory (e.g. ms-playwright/webkit-1852/Playwright.exe).
            // There is no nested revision sub-directory.
            "webkit"   => OperatingSystem.IsWindows() ? "Playwright.exe" : "pw_run.sh",
            _          => browserType
        };
}
