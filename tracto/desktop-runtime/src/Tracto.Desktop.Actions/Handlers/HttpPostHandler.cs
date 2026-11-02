#nullable enable
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Tracto.Desktop.Actions.Handlers;

/// <summary>
/// Performs an HTTP POST request with a JSON body and returns the response.
/// Action name: http.post
/// Inputs:
///   url         (string, required)
///   body        (string, optional) — request body, sent as-is
///   contentType (string, optional, default application/json)
///   timeoutMs   (int, optional, default 30000)
/// Outputs:
///   statusCode (int)    — HTTP status code
///   body       (string) — response body text
///   json       (object) — parsed JSON (only when body is valid JSON)
///   &lt;name&gt;     — every top-level string/number/bool property of a JSON object response,
///                 so later steps can use them as ${storeAs.name}
/// </summary>
public sealed class HttpPostHandler : IActionHandler
{
    private readonly ILogger<HttpPostHandler> _logger;

    public string ActionName => "http.post";

    public HttpPostHandler(ILogger<HttpPostHandler> logger) => _logger = logger;

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx)
    {
        var url = ctx.GetString("url");
        if (string.IsNullOrWhiteSpace(url))
            return ActionResult.Fail("CONFIGURATION", "Input 'url' is required");

        var contentType = ctx.GetString("contentType") ?? "application/json";
        var timeoutMs   = ctx.GetInt("timeoutMs", 30000);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);
        cts.CancelAfter(timeoutMs);

        try
        {
            using var content = new StringContent(ctx.GetString("body") ?? string.Empty, Encoding.UTF8, contentType);
            var response = await ctx.HttpClient.PostAsync(url, content, cts.Token);
            var body     = await response.Content.ReadAsStringAsync(cts.Token);
            var status   = (int)response.StatusCode;

            _logger.LogInformation("http.post {Url} → {Status}", url, status);

            var outputs = new Dictionary<string, object?>
            {
                ["statusCode"] = status,
                ["body"]       = body
            };

            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    var root = JsonDocument.Parse(body).RootElement.Clone();
                    outputs["json"] = root;
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var p in root.EnumerateObject())
                        {
                            if (outputs.ContainsKey(p.Name)) continue;
                            switch (p.Value.ValueKind)
                            {
                                case JsonValueKind.String: outputs[p.Name] = p.Value.GetString(); break;
                                case JsonValueKind.Number:
                                case JsonValueKind.True:
                                case JsonValueKind.False:  outputs[p.Name] = p.Value.ToString(); break;
                            }
                        }
                    }
                }
                catch (JsonException)
                {
                    // body is not JSON — json output omitted
                }
            }

            if (!response.IsSuccessStatusCode)
                return ActionResult.Fail("EXECUTION_FAILED", $"HTTP {status}: {body}") with { Outputs = outputs };

            return ActionResult.Ok(outputs);
        }
        catch (OperationCanceledException) when (!ctx.CancellationToken.IsCancellationRequested)
        {
            return ActionResult.Fail("TIMEOUT", $"http.post timed out after {timeoutMs}ms: {url}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "http.post failed: {Url}", url);
            return ActionResult.Fail("EXECUTION_FAILED", ex.Message);
        }
    }
}
