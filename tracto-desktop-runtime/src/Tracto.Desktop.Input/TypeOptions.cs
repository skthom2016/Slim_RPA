using Tracto.Desktop.Element;

namespace Tracto.Desktop.Input;

/// <summary>
/// Parameters for a type/text-entry operation.
/// </summary>
public sealed record TypeOptions(
    ElementRef Target,
    string Text,
    TypeStrategy? Preferred = null);
