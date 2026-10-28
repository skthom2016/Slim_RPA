#nullable enable
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Tracto.Desktop.Actions.Handlers;

/// <summary>
/// Performs an HTTP GET request and returns the response body.
/// Action name: http.get
/// Inputs:
///   url       (string, required) — full URL including query string
///   timeoutMs (int, optional, default 30000)
/// Outputs:
///   statusCode (int)    — HTTP status code
///   body       (string) — response body text
///   json       (object) — parsed JSON (only when body is valid JSON)
/// </summary>
public sealed class HttpGetHandler : IActionHandler
{
    private readonly ILogger<HttpGetHandler> _logger;

    public string ActionName => "http.get";

    public HttpGetHandler(ILogger<HttpGetHandler> logger) => _logger = logger;

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx)
    {
        var url = ctx.GetString("url");
        if (string.IsNullOrWhiteSpace(url))
            return ActionResult.Fail("CONFIGURATION", "Input 'url' is required");

        var timeoutMs = ctx.GetInt("timeoutMs", 30000);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);
        cts.CancelAfter(timeoutMs);

        try
        {
            var response = await ctx.HttpClient.GetAsync(url, cts.Token);
            var body     = await response.Content.ReadAsStringAsync(cts.Token);
            var status   = (int)response.StatusCode;

            _logger.LogInformation("http.get {Url} → {Status}", url, status);

            var outputs = new Dictionary<string, object?>
            {
                ["statusCode"] = status,
                ["body"]       = body
            };

            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    var doc = JsonDocument.Parse(body);
                    outputs["json"] = doc.RootElement.Clone();
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
            return ActionResult.Fail("TIMEOUT", $"http.get timed out after {timeoutMs}ms: {url}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "http.get failed: {Url}", url);
            return ActionResult.Fail("EXECUTION_FAILED", ex.Message);
        }
    }
}
