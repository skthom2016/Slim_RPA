namespace Tracto.Designer.Core.Validation;

public sealed class ValidationResult
{
    public bool IsValid { get; init; }
    public bool HasWarnings { get; init; }
    public IReadOnlyList<ValidationError> Errors { get; init; } = [];
}
