using System.Text.Json;

namespace Tracto.Desktop.Telemetry;

/// <summary>
/// Appends uncertainty journal entries to a per-attempt JSONL file so that
/// information about UNKNOWN-outcome steps survives a process restart.
/// </summary>
public sealed class JournalWriter
{
    private readonly string _journalRootDir;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    /// <param name="journalRootDir">
    /// Root directory that contains the <c>journal/</c> subdirectory.
    /// Journal files are written to <c>{journalRootDir}/journal/{attemptId}.jsonl</c>.
    /// </param>
    public JournalWriter(string journalRootDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalRootDir);
        _journalRootDir = journalRootDir;
    }

    /// <summary>
    /// Appends an uncertainty entry to the journal file for the specified attempt.
    /// The file is created if it does not exist. Entries are readable by a new
    /// <see cref="JournalWriter"/> instance that targets the same root directory.
    /// </summary>
    /// <param name="attemptId">Unique identifier for the run attempt.</param>
    /// <param name="stepId">The step that produced an UNKNOWN outcome.</param>
    /// <param name="lastAction">The last action executed before the outcome became unknown.</param>
    /// <param name="unknownReason">CEC-approved unknownReason code.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task WriteUncertaintyAsync(
        string attemptId,
        string stepId,
        string lastAction,
        string unknownReason,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var entry = new JournalEntry
        {
            AttemptId     = attemptId,
            StepId        = stepId,
            LastAction    = lastAction,
            UnknownReason = unknownReason,
            TimestampUtc  = DateTimeOffset.UtcNow,
        };

        var line = JsonSerializer.Serialize(entry, JsonOptions);
        var path = GetJournalPath(attemptId);

        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        await File.AppendAllTextAsync(path, line + Environment.NewLine, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads all journal entries for the specified attempt from disk.
    /// Returns an empty array when no file exists.
    /// </summary>
    /// <param name="attemptId">The attempt whose journal to read.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<JournalEntry[]> ReadEntriesAsync(string attemptId, CancellationToken ct)
    {
        var path = GetJournalPath(attemptId);
        if (!File.Exists(path))
            return [];

        var lines = await File.ReadAllLinesAsync(path, ct).ConfigureAwait(false);
        var entries = new List<JournalEntry>(lines.Length);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var entry = JsonSerializer.Deserialize<JournalEntry>(line, JsonOptions);
            if (entry is not null)
                entries.Add(entry);
        }

        return [.. entries];
    }

    private string GetJournalPath(string attemptId) =>
        Path.Combine(_journalRootDir, "journal", $"{SanitiseId(attemptId)}.jsonl");

    private static string SanitiseId(string id) =>
        string.Concat(id.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
}

/// <summary>
/// A single journal entry written for a step with an UNKNOWN outcome.
/// </summary>
public sealed class JournalEntry
{
    public string AttemptId     { get; init; } = string.Empty;
    public string StepId        { get; init; } = string.Empty;
    public string LastAction    { get; init; } = string.Empty;
    public string UnknownReason { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; }
}
