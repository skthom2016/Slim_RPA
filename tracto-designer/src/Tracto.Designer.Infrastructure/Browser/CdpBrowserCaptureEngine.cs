namespace Tracto.Designer.Infrastructure.Browser;

using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Tracto.Designer.Core.Browser;

/// <summary>
/// CDP-based browser capture engine. Implements DS-03-AC01 through DS-03-AC04.
/// Injects a JavaScript listener that observes click, keypress, and change events,
/// reporting them back via Runtime.consoleAPICalled so that no CDP domain that is
/// not natively supported needs to be invented.
/// </summary>
public sealed class CdpBrowserCaptureEngine : IBrowserCaptureEngine
{
    // Unique sentinel prefix so we do not misinterpret page-originated console.log output.
    private const string TractoPrefix = "\u0000TRACTO:";

    private const string ListenerScript = """
        (function () {
          var PREFIX = '\u0000TRACTO:';
          function send(type, data) {
            try { console.log(PREFIX + JSON.stringify({ t: type, d: data })); } catch (e) { }
          }
          document.addEventListener('click', function (e) {
            var el = e.target;
            send('click', {
              sel: el.id ? '#' + el.id : el.tagName.toLowerCase(),
              txt: (el.innerText || el.textContent || '').substring(0, 100)
            });
          }, true);
          document.addEventListener('keypress', function (e) {
            send('key', {
              key: e.key,
              sel: e.target.id ? '#' + e.target.id : e.target.tagName.toLowerCase()
            });
          }, true);
          document.addEventListener('change', function (e) {
            var el = e.target;
            var isPwd = el.type === 'password';
            send('input', {
              sel: el.id ? '#' + el.id : (el.name || el.tagName.toLowerCase()),
              val: isPwd ? null : el.value,
              pwd: isPwd
            });
          }, true);
        })();
        """;

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private int _cmdId;

    public bool IsRunning => _ws?.State == WebSocketState.Open;
    public event EventHandler<BrowserRawEvent>? EventCaptured;

    public async Task StartAsync(BrowserCaptureOptions options, CancellationToken ct = default)
    {
        if (IsRunning) return;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            using var http = new System.Net.Http.HttpClient();
            var listJson = await http.GetStringAsync(
                $"http://localhost:{options.DebugPort}/json/list", _cts.Token)
                .ConfigureAwait(false);
            var tabs = JsonDocument.Parse(listJson).RootElement;
            string? wsUrl = null;
            foreach (var tab in tabs.EnumerateArray())
            {
                var url = tab.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                if (options.TargetUrl is null ||
                    url.Contains(options.TargetUrl, StringComparison.OrdinalIgnoreCase))
                {
                    wsUrl = tab.TryGetProperty("webSocketDebuggerUrl", out var w)
                        ? w.GetString()
                        : null;
                    break;
                }
            }

            if (wsUrl is null)
                throw new InvalidOperationException(
                    $"No browser tab found on port {options.DebugPort}.");

            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(new Uri(wsUrl), _cts.Token).ConfigureAwait(false);

            // Enable domains required for navigation, DOM, and JS-reported events.
            await SendCommandAsync("Page.enable").ConfigureAwait(false);
            await SendCommandAsync("DOM.enable").ConfigureAwait(false);
            await SendCommandAsync("Runtime.enable").ConfigureAwait(false);

            // Inject listener into every document (current and future navigations).
            await SendCommandAsync("Page.addScriptToEvaluateOnNewDocument",
                new { source = ListenerScript }).ConfigureAwait(false);

            // Also evaluate in the current document so capture begins immediately.
            await SendCommandAsync("Runtime.evaluate",
                new { expression = ListenerScript, silent = true }).ConfigureAwait(false);

            _receiveTask = ReceiveLoopAsync(_cts.Token);
        }
        catch
        {
            await StopAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        _cts?.Cancel();
        if (_ws is not null)
        {
            try
            {
                if (_ws.State == WebSocketState.Open)
                    await _ws.CloseAsync(
                        WebSocketCloseStatus.NormalClosure, "",
                        CancellationToken.None).ConfigureAwait(false);
            }
            catch { }
            _ws.Dispose();
            _ws = null;
        }
        if (_receiveTask is not null)
        {
            try { await _receiveTask.ConfigureAwait(false); }
            catch { }
            _receiveTask = null;
        }
        _cts?.Dispose();
        _cts = null;
    }

    private async Task SendCommandAsync(string method, object? @params = null)
    {
        if (_ws is null) return;
        var id = Interlocked.Increment(ref _cmdId);
        var cmd = @params is null
            ? JsonSerializer.Serialize(new { id, method })
            : JsonSerializer.Serialize(new { id, method, @params });
        var bytes = Encoding.UTF8.GetBytes(cmd);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true,
            _cts?.Token ?? default).ConfigureAwait(false);
    }

    /// <summary>
    /// Accumulates WebSocket frames until EndOfMessage before decoding, so that
    /// large CDP messages (common for DOM events) are not silently truncated.
    /// </summary>
    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[16384];
        while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
        {
            try
            {
                ms.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var msg = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
                ProcessMessage(msg);
            }
            catch (OperationCanceledException) { break; }
            catch { /* ignore transient errors */ }
        }
    }

    private void ProcessMessage(string msg)
    {
        try
        {
            var doc = JsonDocument.Parse(msg).RootElement;
            if (!doc.TryGetProperty("method", out var method)) return;
            var m = method.GetString();
            BrowserRawEvent? evt = m switch
            {
                "Page.navigatedWithinDocument" or "Page.frameNavigated" =>
                    new(DateTimeOffset.UtcNow, BrowserEventType.Navigate,
                        doc.TryGetProperty("params", out var p) &&
                        p.TryGetProperty("url", out var u)
                            ? u.GetString() ?? ""
                            : "",
                        null, null, null, false),
                "DOM.documentUpdated" =>
                    new(DateTimeOffset.UtcNow, BrowserEventType.PageLoad,
                        "", null, null, null, false),
                "Runtime.consoleAPICalled" => ParseTractoConsoleEvent(doc),
                _ => null
            };
            if (evt is not null) EventCaptured?.Invoke(this, evt);
        }
        catch { }
    }

    /// <summary>
    /// Parses a Runtime.consoleAPICalled event and extracts a BrowserRawEvent
    /// when the log argument begins with the TractoPrefix sentinel, indicating
    /// it originated from the injected listener script (DS-03-AC03, DS-03-AC04).
    /// </summary>
    private static BrowserRawEvent? ParseTractoConsoleEvent(JsonElement doc)
    {
        if (!doc.TryGetProperty("params", out var p)) return null;
        if (!p.TryGetProperty("args", out var args)) return null;

        foreach (var arg in args.EnumerateArray())
        {
            if (arg.ValueKind != JsonValueKind.Object) continue;
            if (!arg.TryGetProperty("value", out var val)) continue;
            var text = val.GetString();
            if (text is null || !text.StartsWith(TractoPrefix, StringComparison.Ordinal))
                continue;

            try
            {
                using var parsed = JsonDocument.Parse(text[TractoPrefix.Length..]);
                var root = parsed.RootElement;
                if (!root.TryGetProperty("t", out var typeProp)) continue;
                var type = typeProp.GetString();
                if (!root.TryGetProperty("d", out var data)) continue;

                var now = DateTimeOffset.UtcNow;
                var selector = data.TryGetProperty("sel", out var sel) ? sel.GetString() : null;

                if (type == "click")
                {
                    var txt = data.TryGetProperty("txt", out var txtProp)
                        ? txtProp.GetString() : null;
                    return new(now, BrowserEventType.Click, "", selector, txt, null, false);
                }
                if (type == "key")
                {
                    var key = data.TryGetProperty("key", out var keyProp)
                        ? keyProp.GetString() : null;
                    return new(now, BrowserEventType.KeyPress, "", selector, key, null, false);
                }
                if (type == "input")
                {
                    var value = data.TryGetProperty("val", out var valProp)
                        ? valProp.GetString() : null;
                    var isPwd = data.TryGetProperty("pwd", out var pwdProp)
                        && pwdProp.ValueKind == JsonValueKind.True;
                    return new(now, BrowserEventType.FormInput, "", selector, null, value, isPwd);
                }
            }
            catch { }
        }
        return null;
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();
}
