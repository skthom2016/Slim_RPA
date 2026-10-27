using System.Windows.Automation;
using Tracto.Desktop.Element;

namespace Tracto.Desktop.Controls;

/// <summary>
/// Reads the current display value of a UIA control.
/// Tries <c>ValuePattern</c> first; falls back to the UIA <c>Name</c> property.
/// </summary>
public sealed class ControlReadback
{
    private readonly IUiaPatternProvider _patterns;

    public ControlReadback(IUiaPatternProvider patterns)
    {
        _patterns = patterns ?? throw new ArgumentNullException(nameof(patterns));
    }

    /// <summary>
    /// Returns the current text value of <paramref name="el"/>, or <c>null</c> when neither
    /// ValuePattern nor a non-empty Name property is available.
    /// </summary>
    public Task<string?> ReadValueAsync(ElementRef el, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(el);
        ct.ThrowIfCancellationRequested();

        if (_patterns.GetPattern<ValuePattern>(el) is not null)
        {
            string? value = _patterns.GetValue(el);
            return Task.FromResult(value);
        }

        string name = _patterns.GetName(el);
        return Task.FromResult<string?>(string.IsNullOrEmpty(name) ? null : name);
    }
}
