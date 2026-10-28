#nullable enable
using Microsoft.Extensions.Logging;

namespace Tracto.Desktop.Actions.Handlers;

/// <summary>
/// Pauses script execution for a fixed duration.
/// Useful for waiting for windows/dialogs to appear after a prior action.
///
/// Action name: delay
/// Inputs:
///   ms (int, required) — milliseconds to wait (capped at 30000)
/// </summary>
public sealed class DelayHandler : IActionHandler
{
    private readonly ILogger<DelayHandler> _logger;

    public string ActionName => "delay";

    public DelayHandler(ILogger<DelayHandler> logger) => _logger = logger;

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx)
    {
        var ms = Math.Clamp(ctx.GetInt("ms", 1000), 0, 30_000);
        _logger.LogInformation("delay: waiting {Ms} ms", ms);
        await Task.Delay(ms, ctx.CancellationToken);
        return ActionResult.Ok();
    }
}
