namespace Tracto.Designer.Core.Validation;

public sealed class ScriptValidationResult
{
    public IReadOnlyList<ValidationError> Errors { get; }
    public bool IsValid => Errors.All(e => e.Severity != ValidationSeverity.Error);
    public bool HasWarnings => Errors.Any(e => e.Severity == ValidationSeverity.Warning);

    public ScriptValidationResult(IEnumerable<ValidationError> errors) => Errors = [..errors];

    public static ScriptValidationResult Pass() => new([]);
}
