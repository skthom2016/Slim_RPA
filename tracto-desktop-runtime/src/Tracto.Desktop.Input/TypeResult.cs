namespace Tracto.Desktop.Input;

/// <summary>
/// Outcome produced by <see cref="IKeyboardInputEngine.TypeAsync"/>.
/// </summary>
public sealed record TypeResult(
    TypeStrategy StrategyUsed,
    bool Succeeded,
    string? Evidence);
