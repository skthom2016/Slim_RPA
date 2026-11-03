#nullable enable
using Microsoft.Extensions.Logging;

namespace Tracto.Desktop.Actions.Handlers;

/// <summary>
/// Reads the clipboard text into variables. Pair it with a copy in the source app, for example
/// <c>key Ctrl+A</c> then <c>key Ctrl+C</c> in a Notepad window, to take what the window shows.
///
/// Action name: clipboard.get
/// Inputs: none
/// Outputs (with storeAs "x": ${x.text}, ${x.firstLine}):
///   text       — clipboard text with line endings normalised to \n
///   firstLine  — the first non-empty line
/// Fails (EXECUTION_FAILED) when the clipboard holds no text.
/// </summary>
public sealed class ClipboardGetHandler : IActionHandler
{
    private readonly ILogger<ClipboardGetHandler> _logger;

    public string ActionName => "clipboard.get";

    public ClipboardGetHandler(ILogger<ClipboardGetHandler> logger) => _logger = logger;

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx)
    {
        var text = await GetClipboardTextAsync();
        if (string.IsNullOrEmpty(text))
            return ActionResult.Fail("EXECUTION_FAILED", "The clipboard holds no text.");

        text = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n', ' ', '\t');
        var firstLine = text.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? string.Empty;

        _logger.LogInformation("clipboard.get: {Chars} characters", text.Length);

        return ActionResult.Ok(new Dictionary<string, object?>
        {
            ["text"]      = text,
            ["firstLine"] = firstLine
        });
    }

    // The clipboard only works on an STA thread; the thread pool is MTA. Another process can hold
    // the clipboard briefly, so a few attempts are made.
    private static Task<string?> GetClipboardTextAsync()
    {
        var done = new TaskCompletionSource<string?>();
        var thread = new Thread(() =>
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    done.SetResult(Clipboard.ContainsText() ? Clipboard.GetText() : null);
                    return;
                }
                catch
                {
                    Thread.Sleep(100);
                }
            }
            done.SetResult(null);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }
}
