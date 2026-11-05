namespace Tracto.Designer.Infrastructure.Serialization;

using System.Collections.Generic;
using Tracto.Designer.Core.Scripting;

/// <summary>
/// The JSON shape of a script. A single-runtime script has targetRuntime and steps; a segmented
/// script has params and segments instead, and never a top-level targetRuntime.
/// </summary>
public static class ScriptDocument
{
    public static object Build(Script script) => script.IsSegmented ? Segmented(script) : Flat(script);

    // Property order is part of the published bytes: the Orchestrator hashes the payload and
    // refuses a re-publish of the same version with different bytes.
    private static object Flat(Script script) => new
    {
        id            = script.Id,
        name          = script.Name,
        version       = script.Version,
        targetRuntime = script.TargetRuntime,
        steps         = script.Steps.Select(s => new
        {
            stepNumber  = s.StepNumber,
            action      = s.Action,
            description = s.Description,
            condition   = s.Condition,
            parameters  = s.Parameters
        })
    };

    private static object Segmented(Script script) => new
    {
        id       = script.Id,
        name     = script.Name,
        version  = script.Version,
        @params  = script.Params,
        segments = script.Segments.Select(Segment)
    };

    // mode is written only when set, so a segment that follows the browser host's own
    // configuration stays as it was.
    private static object Segment(ScriptSegment g)
    {
        var doc = new Dictionary<string, object?>
        {
            ["name"] = g.Name,
            ["runtime"] = g.Runtime
        };
        if (!string.IsNullOrWhiteSpace(g.Mode)) doc["mode"] = g.Mode;
        if (!string.IsNullOrWhiteSpace(g.Profile)) doc["profile"] = g.Profile.Trim();
        doc["steps"] = Steps(g.Steps);
        return doc;
    }

    private static IEnumerable<object> Steps(IEnumerable<ScriptStep> steps) =>
        steps.Select(s => (object)new
        {
            stepNumber  = s.StepNumber,
            description = s.Description,
            action      = s.Action,
            condition   = s.Condition,
            parameters  = s.Parameters
        });
}
