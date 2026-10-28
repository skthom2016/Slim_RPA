#nullable enable
using Microsoft.Extensions.Logging;

namespace Tracto.Desktop.Actions;

/// <summary>
/// Routes a step's action name to the registered handler and executes it.
/// </summary>
public sealed class ActionDispatcher
{
    private readonly IReadOnlyDictionary<string, IActionHandler> _handlers;
    private readonly ILogger<ActionDispatcher> _logger;

    public ActionDispatcher(IEnumerable<IActionHandler> handlers, ILogger<ActionDispatcher> logger)
    {
        _handlers = handlers.ToDictionary(h => h.ActionName, StringComparer.OrdinalIgnoreCase);
        _logger   = logger;
    }

    public async Task<ActionResult> DispatchAsync(ActionContext ctx)
    {
        if (!_handlers.TryGetValue(ctx.StepId, out var handler))
        {
            // ctx.StepId here is the action name from the step definition
            _logger.LogWarning("No handler registered for action '{Action}'", ctx.StepId);
            return ActionResult.Fail("CONFIGURATION", $"UnknownAction:{ctx.StepId}");
        }
        return await handler.ExecuteAsync(ctx);
    }

    public async Task<ActionResult> DispatchByActionAsync(string actionName, ActionContext ctx)
    {
        if (!_handlers.TryGetValue(actionName, out var handler))
        {
            _logger.LogWarning("No handler registered for action '{Action}'", actionName);
            return ActionResult.Fail("CONFIGURATION", $"UnknownAction:{actionName}");
        }
        return await handler.ExecuteAsync(ctx);
    }
}
