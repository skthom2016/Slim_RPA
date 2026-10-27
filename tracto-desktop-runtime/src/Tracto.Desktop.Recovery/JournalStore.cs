using System.Text.Json;

namespace Tracto.Desktop.Recovery;

/// <summary>
/// Durable, append-only store for <see cref="JournalEntry"/> records.
/// <para>
/// Each attempt gets its own JSONL file at <c>{Root}/journal/{attemptId}.jsonl</c>.
/// The file survives a Runtime restart; the Executor reads it during reconciliation.
/// </para>
/// </summary>
public sealed class JournalStore
{
    private static readonly JsonSerializerOptions s_options =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    /// <summary>Gets the root directory under which the <c>journal/</c> sub-folder is created.</summary>
    public string Root { get; }

    /// <summary>
    /// Initialises a journal store rooted at <paramref name="root"/>.
    /// </summary>
    /// <param name="root">
    /// Absolute path to the root directory. A <c>journal/</c> sub-directory is created automatically.
    /// </param>
    public JournalStore(string root)
    {
        Root = root;
    }

    /// <summary>
    /// Appends <paramref name="entry"/> as a JSON line to the attempt's journal file.
    /// Creates the journal directory and file if they do not exist.
    /// </summary>
    public async Task AppendAsync(JournalEntry entry)
    {
        var dir = Path.Combine(Root, "journal");
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, $"{entry.AttemptId}.jsonl");
        var line = JsonSerializer.Serialize(entry, s_options) + Environment.NewLine;

        // File.AppendAllTextAsync creates the file if missing and is async-friendly.
        await File.AppendAllTextAsync(path, line).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads all journal entries for <paramref name="attemptId"/> from disk.
    /// Returns an empty list when no journal file exists yet.
    /// </summary>
    public async Task<IReadOnlyList<JournalEntry>> ReadAllAsync(string attemptId)
    {
        var path = Path.Combine(Root, "journal", $"{attemptId}.jsonl");
        if (!File.Exists(path))
            return Array.Empty<JournalEntry>();

        var lines = await File.ReadAllLinesAsync(path).ConfigureAwait(false);
        var entries = new List<JournalEntry>(lines.Length);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var entry = JsonSerializer.Deserialize<JournalEntry>(line, s_options);
            if (entry is not null)
                entries.Add(entry);
        }

        return entries;
    }
}
