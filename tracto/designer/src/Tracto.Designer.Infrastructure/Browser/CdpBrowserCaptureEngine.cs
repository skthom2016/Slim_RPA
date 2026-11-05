namespace Tracto.Designer.Infrastructure.Browser;

using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Tracto.Designer.Core.Browser;

/// <summary>
/// Records what an author does in a browser through the Chrome DevTools Protocol.
/// A listener injected into every page reports clicks, field changes and Enter/Tab/Escape
/// presses with the element's accessible identity (role, name, label, placeholder, test id,
/// text and a CSS fallback) so the draft can use the same locators the Browser Runtime uses.
/// Main-frame navigations are reported from the protocol itself.
/// </summary>
public sealed class CdpBrowserCaptureEngine : IBrowserCaptureEngine
{
    // Unique sentinel prefix so page-originated console.log output is never misread.
    private const string TractoPrefix = "\u0000TRACTO:";

    private const string ListenerScript = """
        (function () {
          if (window.__tractoRecorder) return;
          window.__tractoRecorder = true;
          var PREFIX = '\u0000TRACTO:';
          function send(type, data) {
            try { console.log(PREFIX + JSON.stringify({ t: type, d: data })); } catch (e) { }
          }
          function text(el) { return ((el && (el.innerText || el.textContent)) || '').replace(/\s+/g, ' ').trim(); }
          function roleOf(el) {
            var r = el.getAttribute('role'); if (r) return r;
            var t = el.tagName.toLowerCase(), ty = (el.type || '').toLowerCase();
            if (t === 'button' || (t === 'input' && (ty === 'button' || ty === 'submit' || ty === 'reset' || ty === 'image'))) return 'button';
            if (t === 'a' && el.hasAttribute('href')) return 'link';
            if (t === 'input') {
              if (ty === 'checkbox') return 'checkbox';
              if (ty === 'radio') return 'radio';
              if (ty === 'search') return 'searchbox';
              if (ty === '' || ty === 'text' || ty === 'email' || ty === 'tel' || ty === 'url' || ty === 'password' || ty === 'number') return 'textbox';
              return null;
            }
            if (t === 'textarea') return 'textbox';
            if (t === 'select') return 'combobox';
            if (/^h[1-6]$/.test(t)) return 'heading';
            return null;
          }
          function labelOf(el) {
            if (el.labels && el.labels.length) return text(el.labels[0]);
            var a = el.getAttribute('aria-label'); if (a) return a.trim();
            var lb = el.getAttribute('aria-labelledby');
            if (lb) { var n = document.getElementById(lb); if (n) return text(n); }
            return '';
          }
          function nameOf(el) {
            var a = el.getAttribute('aria-label'); if (a) return a.trim();
            var lb = el.getAttribute('aria-labelledby');
            if (lb) { var n = document.getElementById(lb); if (n) return text(n); }
            if (el.labels && el.labels.length) return text(el.labels[0]);
            var t = el.tagName.toLowerCase();
            if (t === 'input' && (el.type === 'submit' || el.type === 'button' || el.type === 'reset')) return (el.value || '').trim();
            if (t === 'input' || t === 'textarea' || t === 'select') return (el.placeholder || el.title || '').trim();
            if (t === 'img') return (el.alt || '').trim();
            var s = text(el);
            return s.length <= 80 ? s : '';
          }
          function cssOf(el) {
            if (el.id) return '#' + CSS.escape(el.id);
            var t = el.tagName.toLowerCase();
            if (el.name) return t + '[name="' + el.name.replace(/"/g, '\\"') + '"]';
            var path = [], cur = el;
            while (cur && cur.nodeType === 1 && path.length < 6) {
              if (cur.id) { path.unshift('#' + CSS.escape(cur.id)); break; }
              var seg = cur.tagName.toLowerCase(), p = cur.parentElement;
              if (p) {
                var same = Array.prototype.filter.call(p.children, function (c) { return c.tagName === cur.tagName; });
                if (same.length > 1) seg += ':nth-of-type(' + (same.indexOf(cur) + 1) + ')';
              }
              path.unshift(seg); cur = p;
            }
            return path.join(' > ');
          }
          function describe(el) {
            var t = el.tagName.toLowerCase();
            var textual = (t === 'a' || t === 'button' || t === 'span' || t === 'label' || t === 'li' || t === 'div' || /^h[1-6]$/.test(t)) ? text(el) : '';
            return {
              role: roleOf(el), name: nameOf(el) || null, label: labelOf(el) || null,
              placeholder: el.placeholder || null, testId: el.getAttribute('data-testid') || null,
              text: textual && textual.length <= 80 ? textual : null, css: cssOf(el),
              tag: t, type: (el.type || '').toLowerCase() || null
            };
          }
          function interactive(el) {
            return (el.closest && el.closest('button,a[href],input,select,textarea,summary,[role="button"],[role="link"],[role="menuitem"],[role="tab"],[role="checkbox"],[role="option"],label')) || el;
          }
          function isField(el) {
            var t = el.tagName.toLowerCase();
            return t === 'textarea' || t === 'select' || (t === 'input' && !/^(button|submit|reset|image|file|hidden)$/.test((el.type || '').toLowerCase()));
          }
          function reportValue(el) {
            var ty = (el.type || '').toLowerCase();
            var val = (ty === 'checkbox' || ty === 'radio') ? String(el.checked) : el.value;
            if (el.__tractoLast === val) return;
            el.__tractoLast = val;
            var pwd = ty === 'password';
            send('input', { tg: describe(el), val: pwd ? null : val, pwd: pwd });
          }
          document.addEventListener('click', function (e) {
            var el = interactive(e.target);
            if (!el || el.nodeType !== 1) return;
            if (isField(el) && !/^(checkbox|radio)$/.test((el.type || '').toLowerCase())) return; // focusing a field is not a step
            if (/^(checkbox|radio)$/.test((el.type || '').toLowerCase())) return;                   // reported by change
            send('click', { tg: describe(el) });
          }, true);
          document.addEventListener('keydown', function (e) {
            if (e.key !== 'Enter' && e.key !== 'Tab' && e.key !== 'Escape') return;
            var el = e.target;
            if (el && el.nodeType === 1 && isField(el)) reportValue(el);
            send('key', { key: e.key, tg: (el && el.nodeType === 1) ? describe(interactive(el)) : null });
          }, true);
          document.addEventListener('change', function (e) {
            var el = e.target;
            if (el && el.nodeType === 1 && isField(el)) reportValue(el);
          }, true);
        })();
        """;

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private BrowserLauncher? _launcher;
    private int _cmdId;

    public bool IsRunning => _ws?.State == WebSocketState.Open;
    public event EventHandler<BrowserRawEvent>? EventCaptured;

    public async Task StartAsync(BrowserCaptureOptions options, CancellationToken ct = default)
    {
        if (IsRunning) return;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            if (options.LaunchBrowser)
            {
                _launcher = new BrowserLauncher();
                _launcher.Launch(options.BrowserType, options.DebugPort, options.StartUrl, options.UserDataDir);
            }

            var (wsUrl, pageUrl) = await FindPageWebSocketAsync(options, _cts.Token).ConfigureAwait(false);

            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(new Uri(wsUrl), _cts.Token).ConfigureAwait(false);

            await SendCommandAsync("Page.enable").ConfigureAwait(false);
            await SendCommandAsync("Runtime.enable").ConfigureAwait(false);

            // Inject the listener into every document (current and future navigations).
            await SendCommandAsync("Page.addScriptToEvaluateOnNewDocument",
                new { source = ListenerScript }).ConfigureAwait(false);
            await SendCommandAsync("Runtime.evaluate",
                new { expression = ListenerScript, silent = true }).ConfigureAwait(false);

            _receiveTask = ReceiveLoopAsync(_cts.Token);

            // The page the recording starts on was loaded before this client attached, so
            // its navigation is reported here; later navigations arrive from the protocol.
            if (!string.IsNullOrWhiteSpace(pageUrl) &&
                !pageUrl.StartsWith("about:", StringComparison.OrdinalIgnoreCase) &&
                !pageUrl.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase) &&
                !pageUrl.StartsWith("edge://", StringComparison.OrdinalIgnoreCase))
                EventCaptured?.Invoke(this, new BrowserRawEvent(
                    DateTimeOffset.UtcNow, BrowserEventType.Navigate, pageUrl, null, null, null, false));
        }
        catch
        {
            await StopAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Waits for the DevTools endpoint and picks the page tab to record.</summary>
    private static async Task<(string WebSocketUrl, string PageUrl)> FindPageWebSocketAsync(BrowserCaptureOptions options, CancellationToken ct)
    {
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var deadline = DateTime.UtcNow.AddSeconds(options.LaunchBrowser ? 20 : 3);
        Exception? last = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var listJson = await http.GetStringAsync($"http://localhost:{options.DebugPort}/json/list", ct)
                    .ConfigureAwait(false);
                using var doc = JsonDocument.Parse(listJson);
                (string, string)? fallback = null;
                foreach (var tab in doc.RootElement.EnumerateArray())
                {
                    var type = tab.TryGetProperty("type", out var ty) ? ty.GetString() : null;
                    if (type != "page") continue;
                    var url = tab.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                    var ws = tab.TryGetProperty("webSocketDebuggerUrl", out var w) ? w.GetString() : null;
                    if (ws is null) continue;
                    if (options.TargetUrl is null || url.Contains(options.TargetUrl, StringComparison.OrdinalIgnoreCase))
                        return (ws, url);
                    fallback ??= (ws, url);
                }
                if (fallback is { } fb) return fb;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
            }
            await Task.Delay(250, ct).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            $"No browser page found on DevTools port {options.DebugPort}." +
            (options.LaunchBrowser ? " The launched browser did not open its DevTools endpoint." :
                " Start the browser with --remote-debugging-port=" + options.DebugPort + " or tick Launch."),
            last);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        _cts?.Cancel();
        if (_ws is not null)
        {
            try
            {
                if (_ws.State == WebSocketState.Open)
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None)
                        .ConfigureAwait(false);
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
        _launcher?.Close();
        _launcher = null;
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
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, _cts?.Token ?? default).ConfigureAwait(false);
    }

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

                ProcessMessage(Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length));
            }
            catch (OperationCanceledException) { break; }
            catch { /* ignore transient errors */ }
        }
    }

    private void ProcessMessage(string msg)
    {
        try
        {
            using var parsed = JsonDocument.Parse(msg);
            var doc = parsed.RootElement;
            if (!doc.TryGetProperty("method", out var method)) return;

            BrowserRawEvent? evt = method.GetString() switch
            {
                "Page.frameNavigated" => MainFrameNavigation(doc),
                "Runtime.consoleAPICalled" => ParseTractoConsoleEvent(doc),
                _ => null
            };
            if (evt is not null) EventCaptured?.Invoke(this, evt);
        }
        catch { }
    }

    private static BrowserRawEvent? MainFrameNavigation(JsonElement doc)
    {
        if (!doc.TryGetProperty("params", out var p) || !p.TryGetProperty("frame", out var frame)) return null;
        if (frame.TryGetProperty("parentId", out _)) return null; // iframe
        var url = frame.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
        return new BrowserRawEvent(DateTimeOffset.UtcNow, BrowserEventType.Navigate, url, null, null, null, false);
    }

    private static BrowserRawEvent? ParseTractoConsoleEvent(JsonElement doc)
    {
        if (!doc.TryGetProperty("params", out var p) || !p.TryGetProperty("args", out var args)) return null;

        foreach (var arg in args.EnumerateArray())
        {
            if (arg.ValueKind != JsonValueKind.Object || !arg.TryGetProperty("value", out var val)) continue;
            var text = val.GetString();
            if (text is null || !text.StartsWith(TractoPrefix, StringComparison.Ordinal)) continue;

            try
            {
                using var parsed = JsonDocument.Parse(text[TractoPrefix.Length..]);
                var root = parsed.RootElement;
                if (!root.TryGetProperty("t", out var typeProp) || !root.TryGetProperty("d", out var data)) continue;

                var now = DateTimeOffset.UtcNow;
                var target = data.TryGetProperty("tg", out var tg) && tg.ValueKind == JsonValueKind.Object
                    ? ParseTarget(tg) : null;

                switch (typeProp.GetString())
                {
                    case "click":
                        return new BrowserRawEvent(now, BrowserEventType.Click, "", target?.Css,
                            target?.Name ?? target?.Text, null, false, target);
                    case "key":
                        var key = data.TryGetProperty("key", out var keyProp) ? keyProp.GetString() : null;
                        return new BrowserRawEvent(now, BrowserEventType.KeyPress, "", target?.Css, key, null, false, target);
                    case "input":
                        var value = data.TryGetProperty("val", out var valProp) && valProp.ValueKind == JsonValueKind.String
                            ? valProp.GetString() : null;
                        var isPwd = data.TryGetProperty("pwd", out var pwdProp) && pwdProp.ValueKind == JsonValueKind.True;
                        return new BrowserRawEvent(now, BrowserEventType.FormInput, "", target?.Css,
                            target?.Label ?? target?.Name, value, isPwd, target);
                }
            }
            catch { }
        }
        return null;
    }

    private static BrowserTarget ParseTarget(JsonElement tg)
    {
        string? S(string k) => tg.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        return new BrowserTarget(S("role"), S("name"), S("label"), S("placeholder"), S("testId"), S("text"), S("css"), S("tag"), S("type"));
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();
}
