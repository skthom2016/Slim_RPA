namespace Tracto.Designer.Infrastructure.Browser;

using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Tracto.Designer.Core.Browser;

/// <summary>
/// Opens a browser on one of the Browser Runtime's named profile folders so a person can sign
/// in once. It deliberately starts the browser with no DevTools port: nothing is recorded here,
/// the person is only signing in.
///
/// <para>The folder must be the one the Browser Runtime will open later, so the layout matches
/// the host's: <c>BROWSER_PROFILE_ROOT</c> when set, otherwise
/// <c>%LOCALAPPDATA%\Tracto\browser-profiles\&lt;name&gt;</c>. This works when the Designer and
/// the worker run on the same machine and as the same Windows user, which is the normal setup.</para>
/// </summary>
public sealed partial class BrowserProfileSignIn : IBrowserProfileSignIn
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")]
    private static partial Regex NamePattern();

    public bool IsValidName(string? profileName) =>
        !string.IsNullOrWhiteSpace(profileName) && NamePattern().IsMatch(profileName.Trim());

    public string ResolveFolder(string profileName)
    {
        var name = (profileName ?? string.Empty).Trim();
        if (!IsValidName(name))
            throw new ArgumentException(
                "A profile name uses letters, digits, dash, underscore or dot (1 to 64 characters).",
                nameof(profileName));

        var root = Environment.GetEnvironmentVariable("BROWSER_PROFILE_ROOT");
        if (string.IsNullOrWhiteSpace(root))
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tracto", "browser-profiles");

        var folder = Path.GetFullPath(Path.Combine(root, name));
        var rootFull = Path.GetFullPath(root);
        if (!folder.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Profile '{name}' resolves outside the profile root.", nameof(profileName));

        return folder;
    }

    public async Task SignInAsync(BrowserType browser, string profileName, string? startUrl, CancellationToken ct = default)
    {
        var folder = ResolveFolder(profileName);
        Directory.CreateDirectory(folder);

        var exe = BrowserLauncher.ResolveExecutable(browser);

        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        psi.ArgumentList.Add($"--user-data-dir={folder}");
        psi.ArgumentList.Add("--no-first-run");
        psi.ArgumentList.Add("--no-default-browser-check");
        psi.ArgumentList.Add("--new-window");
        psi.ArgumentList.Add(string.IsNullOrWhiteSpace(startUrl) ? "about:blank" : startUrl);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Could not start {exe}.");

        // The person signs in and closes the window; the browser writes the profile on exit.
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
    }
}
