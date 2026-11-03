#nullable enable
using System.Text;
using Microsoft.Extensions.Logging;

namespace Tracto.Desktop.Actions.Handlers;

/// <summary>
/// Reads a text file into variables so later steps, or the next run of a flow, can use its content.
///
/// Action name: file.read
/// Inputs:
///   path (string, required) — full path of the file; %USERPROFILE%-style variables are expanded
///   maxChars (int, optional) — longest text returned, default 100000
/// Outputs (with storeAs "x": ${x.text}, ${x.firstLine}, ${x.lineCount}):
///   text       — the file content with line endings normalised to \n and trailing blank lines removed
///   firstLine  — the first non-empty line
///   lineCount  — number of lines in text
/// </summary>
public sealed class FileReadHandler : IActionHandler
{
    private const int DefaultMaxChars = 100_000;
    private const long MaxFileBytes = 5 * 1024 * 1024;

    private readonly ILogger<FileReadHandler> _logger;

    public string ActionName => "file.read";

    public FileReadHandler(ILogger<FileReadHandler> logger) => _logger = logger;

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx)
    {
        var path = ctx.GetString("path");
        if (string.IsNullOrWhiteSpace(path))
            return ActionResult.Fail("CONFIGURATION", "Input 'path' is required");

        path = Environment.ExpandEnvironmentVariables(path);
        if (!File.Exists(path))
            return ActionResult.Fail("NOT_FOUND", $"File not found: {path}");

        var info = new FileInfo(path);
        if (info.Length > MaxFileBytes)
            return ActionResult.Fail("EXECUTION_FAILED", $"File is {info.Length} bytes; file.read reads at most {MaxFileBytes}.");

        string raw;
        try
        {
            // detectEncodingFromByteOrderMarks handles UTF-8, UTF-16 and UTF-32 with a BOM;
            // files without one are read as UTF-8.
            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            raw = await reader.ReadToEndAsync(ctx.CancellationToken);
        }
        catch (IOException ex)
        {
            return ActionResult.Fail("EXECUTION_FAILED", $"Could not read {path}: {ex.Message}");
        }

        var text = raw.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n', ' ', '\t');
        var maxChars = Math.Max(1, ctx.GetInt("maxChars", DefaultMaxChars));
        if (text.Length > maxChars) text = text[..maxChars];

        var lines = text.Length == 0 ? [] : text.Split('\n');
        var firstLine = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? string.Empty;

        _logger.LogInformation("file.read: {Chars} characters, {Lines} line(s) from {Path}", text.Length, lines.Length, path);

        return ActionResult.Ok(new Dictionary<string, object?>
        {
            ["text"]      = text,
            ["firstLine"] = firstLine,
            ["lineCount"] = lines.Length.ToString()
        });
    }
}
