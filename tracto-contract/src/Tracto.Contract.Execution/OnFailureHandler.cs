#nullable enable

// Evaluates the onFailure field of a step definition and determines what the run should do.
// ST1-06: onFailure handler governs step failure continuation.

using System.Text.Json;

namespace Tracto.Contract.Execution;

// Evaluates the onFailure field of a step definition and determines what the run should do.
// ST1-06: onFailure handler governs step failure continuation.
public sealed class OnFailureHandler
{
    // Returns the handler action from the step's onFailure JSON element.
    // Supported values: "continue" (proceed to next step), "abort" (fail the run),
    //                   {"goto": "<stepId>"} (jump to another step).
    public OnFailureAction Evaluate(JsonElement? onFailure, string? defaultAction = "abort")
    {
        if (onFailure is null || onFailure.Value.ValueKind == JsonValueKind.Null)
            return defaultAction == "continue"
                ? OnFailureAction.Continue
                : OnFailureAction.Abort;

        if (onFailure.Value.ValueKind == JsonValueKind.String)
        {
            return onFailure.Value.GetString() switch
            {
                "continue" => OnFailureAction.Continue,
                "abort"    => OnFailureAction.Abort,
                _          => OnFailureAction.Abort
            };
        }

        if (onFailure.Value.ValueKind == JsonValueKind.Object
            && onFailure.Value.TryGetProperty("goto", out var gotoEl)
            && gotoEl.ValueKind == JsonValueKind.String)
        {
            return new OnFailureAction(OnFailureKind.GoTo, gotoEl.GetString());
        }

        return OnFailureAction.Abort;
    }
}

public enum OnFailureKind { Continue, Abort, GoTo }

public record OnFailureAction(OnFailureKind Kind, string? TargetStepId = null)
{
    public static OnFailureAction Continue => new(OnFailureKind.Continue);
    public static OnFailureAction Abort    => new(OnFailureKind.Abort);
}
