#nullable enable
using Microsoft.Extensions.Logging;

namespace Tracto.Desktop.Actions;

/// <summary>
/// Context passed to every action handler during execution of a workflow step.
/// </summary>
public sealed class ActionContext
{
    public required string StepId { get; init; }
    public required IReadOnlyDictionary<string, object?> Inputs { get; init; }
    public required IReadOnlyDictionary<string, object?> Vars { get; init; }
    public required HttpClient HttpClient { get; init; }
    public required ILogger Logger { get; init; }
    public required CancellationToken CancellationToken { get; init; }

    public string? GetString(string key)
        => Inputs.TryGetValue(key, out var v) ? v?.ToString() : null;

    public int GetInt(string key, int defaultValue = 0)
        => Inputs.TryGetValue(key, out var v) && v is not null
            ? (int.TryParse(v.ToString(), out var n) ? n : defaultValue)
            : defaultValue;

    public bool GetBool(string key, bool defaultValue = false)
        => Inputs.TryGetValue(key, out var v) && v is not null
            ? (bool.TryParse(v.ToString(), out var b) ? b : defaultValue)
            : defaultValue;
}
