namespace Tracto.Desktop.Controls;

/// <summary>
/// Outcome produced by <see cref="IControlInteractionEngine"/> control-interaction methods.
/// </summary>
/// <param name="Changed">
/// True when the engine actively changed the control's value or state.
/// False when the control was already in the desired state (idempotent no-op).
/// </param>
/// <param name="Strategy">
/// Human-readable strategy token used to apply the change, e.g. "ValuePattern", "Keystroke",
/// "Toggle", "SelectionItemPattern". Useful for diagnostics and evidence (DT-12-AC08).
/// </param>
/// <param name="ReadbackValue">
/// Value read back from the control after the operation, or null when readback was not performed.
/// </param>
public sealed record ControlValueResult(bool Changed, string Strategy, string? ReadbackValue);
