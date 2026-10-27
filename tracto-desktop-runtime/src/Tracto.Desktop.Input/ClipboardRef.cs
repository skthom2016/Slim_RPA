namespace Tracto.Desktop.Input;

/// <summary>
/// An immutable reference token produced by <see cref="IClipboardEngine.CopyAsync"/>.
/// The captured text is NOT stored here; it lives in a <see cref="ClearableBuffer"/>
/// managed by the engine so it can be securely zeroed after use.
/// </summary>
/// <param name="RefName">The caller-supplied name that identifies this captured value.</param>
public sealed record ClipboardRef(string RefName);
