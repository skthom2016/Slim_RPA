namespace Tracto.Designer.Infrastructure.Browser;

using System.Diagnostics;
using System.IO;
using Tracto.Designer.Core.Browser;

/// <summary>
/// Starts a browser with the DevTools port open and a clean, recorder-owned profile, so the
/// Designer can attach to it without the author configuring anything.
/// </summary>
public sealed class BrowserLauncher : IDisposable
{
    private Process? _process;

    public bool IsRunning => _process is { HasExited: false };

    public static string ResolveExecutable(BrowserType type)
    {
        var candidates = type switch
        {
            BrowserType.Chromium => BundledChromiumCandidates(),
            BrowserType.Chrome =>
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
            ],
            BrowserType.Edge =>
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe")
            ],
            _ => throw new NotSupportedException($"{type} cannot be launched by the recorder; use Chromium, Chrome or Edge.")
        };

        return candidates.FirstOrDefault(File.Exists)
               ?? throw new FileNotFoundException(
                   type == BrowserType.Chromium
                       ? "The bundled Chromium is not installed. Start the Browser Runtime host once (it installs it), or choose Chrome or Edge."
                       : $"{type} was not found in its usual install folders.");
    }

    private static IEnumerable<string> BundledChromiumCandidates()
    {
        var home = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH");
        if (string.IsNullOrWhiteSpace(home))
            home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ms-playwright");
        if (!Directory.Exists(home)) yield break;

        // Newest revision first.
        var dirs = Directory.GetDirectories(home, "chromium-*")
            .OrderByDescending(d => int.TryParse(Path.GetFileName(d)["chromium-".Length..], out var n) ? n : 0);
        foreach (var d in dirs)
            yield return Path.Combine(d, "chrome-win", "chrome.exe");
    }

    /// <summary>
    /// Starts the browser with the DevTools port open. <c>userDataDir</c> is the profile folder
    /// to run in: null uses the recorder's own throwaway folder, with nothing signed in, while
    /// the signed-in profile records with those sign-ins already in place.
    /// </summary>
    public void Launch(BrowserType type, int debugPort, string? startUrl, string? userDataDir = null)
    {
        if (IsRunning) return;

        var exe = ResolveExecutable(type);
        var profile = string.IsNullOrWhiteSpace(userDataDir)
            ? Path.Combine(Path.GetTempPath(), "tracto-designer", $"profile-{type.ToString().ToLowerInvariant()}")
            : userDataDir;
        Directory.CreateDirectory(profile);

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false
        };
        psi.ArgumentList.Add($"--remote-debugging-port={debugPort}");
        psi.ArgumentList.Add($"--user-data-dir={profile}");
        psi.ArgumentList.Add("--no-first-run");
        psi.ArgumentList.Add("--no-default-browser-check");
        psi.ArgumentList.Add("--disable-features=Translate");
        psi.ArgumentList.Add("--new-window");
        psi.ArgumentList.Add(string.IsNullOrWhiteSpace(startUrl) ? "about:blank" : startUrl);

        _process = Process.Start(psi)
                   ?? throw new InvalidOperationException($"Could not start {exe}.");
    }

    public void Close()
    {
        if (_process is null) return;
        try
        {
            if (!_process.HasExited)
            {
                _process.CloseMainWindow();
                if (!_process.WaitForExit(3000)) _process.Kill(entireProcessTree: true);
            }
        }
        catch { /* the browser is already gone */ }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    public void Dispose() => Close();
}
