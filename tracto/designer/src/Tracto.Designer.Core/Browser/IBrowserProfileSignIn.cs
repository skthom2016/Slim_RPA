namespace Tracto.Designer.Core.Browser;

/// <summary>
/// Opens a browser on a named profile folder so a person can sign in to the sites a script
/// needs. The Browser Runtime later opens the same folder, so those sign-ins are already there
/// and the script needs no sign-in steps and no password.
/// </summary>
public interface IBrowserProfileSignIn
{
    /// <summary>Letters, digits, dash, underscore and dot, so the name is always a safe folder name.</summary>
    bool IsValidName(string? profileName);

    /// <summary>The folder a profile name maps to, matching what the Browser Runtime uses.</summary>
    string ResolveFolder(string profileName);

    /// <summary>
    /// Opens the browser on that profile and returns when the person closes it. Whatever they
    /// signed into is saved in the folder.
    /// </summary>
    Task SignInAsync(BrowserType browser, string profileName, string? startUrl, CancellationToken ct = default);
}
