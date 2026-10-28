#nullable enable
namespace Tracto.Desktop.Actions;

/// <summary>
/// Result returned by every action handler.
/// </summary>
public sealed record ActionResult(
    bool Success,
    string? ErrorCategory = null,
    string? ErrorDetail   = null,
    IReadOnlyDictionary<string, object?>? Outputs = null)
{
    public static ActionResult Ok(IReadOnlyDictionary<string, object?>? outputs = null)
        => new(true, Outputs: outputs);

    public static ActionResult Fail(string category, string detail)
        => new(false, category, detail);
}
