#nullable enable
namespace Tracto.Desktop.Actions;

/// <summary>
/// Contract every action handler must implement.
/// ActionName is the dotted action identifier used in StepDefinition.action (e.g. "app.launch").
/// </summary>
public interface IActionHandler
{
    string ActionName { get; }
    Task<ActionResult> ExecuteAsync(ActionContext ctx);
}
