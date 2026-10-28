#nullable enable
using Microsoft.Extensions.Logging;

namespace Tracto.Desktop.Actions.Handlers;

/// <summary>
/// Catch-all for unrecognised action names produced by the capture engine
/// (the "action" fallback in ScriptDraftGenerator.MapAction).
/// Logs all inputs and succeeds without touching the desktop.
///
/// Action name: action
/// </summary>
public sealed class CatchAllHandler : IActionHandler
{
    private readonly ILogger<CatchAllHandler> _logger;

    public string ActionName => "action";

    public CatchAllHandler(ILogger<CatchAllHandler> logger) => _logger = logger;

    public Task<ActionResult> ExecuteAsync(ActionContext ctx)
    {
        _logger.LogWarning(
            "catch-all action '{StepId}': no specific handler — inputs: {Inputs}",
            ctx.StepId,
            string.Join(", ", ctx.Inputs.Select(kv => $"{kv.Key}={kv.Value}")));
        return Task.FromResult(ActionResult.Ok());
    }
}
