namespace Tracto.Designer.Infrastructure.Settings;

using System.IO;

/// <summary>
/// Remembers the folder the Import and Export dialogs were last used in, so they open there next
/// time, including after the Designer is restarted. The first time, before any folder has been
/// used, they open in the user's Downloads folder.
/// </summary>
/// <remarks>
/// This is only a convenience. Reading or writing the remembered folder never throws: when it
/// cannot be read or saved the dialogs simply open in the fallback folder.
/// </remarks>
public sealed class LastFolderStore
{
    private readonly string _storePath;
    private readonly Func<string?> _fallback;

    /// <summary>Uses the Designer's settings folder and the user's Downloads folder as the fallback.</summary>
    public LastFolderStore()
        : this(DefaultStorePath(), DefaultFallback)
    {
    }

    /// <param name="storePath">The file the folder is saved in.</param>
    /// <param name="fallback">The folder to open in when none has been remembered or it no longer exists.</param>
    public LastFolderStore(string storePath, Func<string?> fallback)
    {
        _storePath = storePath;
        _fallback = fallback;
    }

    /// <summary>
    /// The folder a dialog should open in: the remembered one when it still exists, otherwise the
    /// fallback when that exists, otherwise null (the dialog then uses the system's own choice).
    /// </summary>
    public string? Get()
    {
        var remembered = Read();
        if (remembered is not null && Directory.Exists(remembered)) return remembered;

        var fallback = _fallback();
        return !string.IsNullOrWhiteSpace(fallback) && Directory.Exists(fallback) ? fallback : null;
    }

    /// <summary>Remembers the folder that contains <paramref name="filePath"/>, the file a dialog just returned.</summary>
    /// <param name="filePath">A file chosen in an Import or Export dialog.</param>
    public void RememberFolderOf(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        string? folder;
        try { folder = Path.GetDirectoryName(Path.GetFullPath(filePath)); }
        catch (Exception) { return; }
        Remember(folder);
    }

    /// <summary>Remembers a folder. An empty name or a folder that does not exist is ignored.</summary>
    /// <param name="folder">The folder to open in next time.</param>
    public void Remember(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
        try
        {
            var dir = Path.GetDirectoryName(_storePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_storePath, folder);
        }
        catch (Exception)
        {
            // A folder that could not be saved only costs the convenience of remembering it.
        }
    }

    private string? Read()
    {
        try
        {
            if (!File.Exists(_storePath)) return null;
            var text = File.ReadAllText(_storePath).Trim();
            return text.Length == 0 ? null : text;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The user's Downloads folder.</summary>
    public static string? DefaultFallback() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    private static string DefaultStorePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Tracto", "Designer", "last-folder.txt");
}
