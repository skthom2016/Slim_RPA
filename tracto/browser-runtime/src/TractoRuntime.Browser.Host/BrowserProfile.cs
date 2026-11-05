using System.Text.RegularExpressions;

namespace TractoRuntime.Browser.Host;

/// <summary>
/// A named browser profile: a folder on this machine that keeps a browser's cookies and
/// local storage between runs, so a site stays signed in.
///
/// <para>A profile is opt-in. A run that names no profile gets the usual throwaway browser
/// with nothing signed in. A run that names one reuses that folder, which is why only one run
/// may use a given profile at a time: the browser locks the folder while it is open.</para>
///
/// <para>The folder belongs to the Windows account the worker runs as. Anyone who can sign in
/// as that account, or read that folder, can act as whoever is signed in inside it.</para>
/// </summary>
public static partial class BrowserProfile
{
    /// <summary>Letters, digits, dash, underscore and dot, so the name is always a safe folder name.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")]
    private static partial Regex NamePattern();

    public static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && NamePattern().IsMatch(name);

    /// <summary>
    /// The folder for a profile name, under <paramref name="root"/>. The name is validated so a
    /// script can never reach outside the root with something like <c>..\..\Windows</c>.
    /// </summary>
    public static string ResolveDirectory(string root, string name)
    {
        if (!IsValidName(name))
            throw new ScriptValidationException(
                $"'profile' is \"{name}\"; use letters, digits, dash, underscore or dot (1 to 64 characters).");

        var dir = Path.GetFullPath(Path.Combine(root, name));
        var rootFull = Path.GetFullPath(root);

        if (!dir.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new ScriptValidationException($"'profile' resolves outside the profile root: {name}");

        return dir;
    }

    /// <summary>The default root: a per-user folder, so profiles are not shared between accounts.</summary>
    public static string DefaultRoot =>
        Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "Tracto", "browser-profiles");
}
