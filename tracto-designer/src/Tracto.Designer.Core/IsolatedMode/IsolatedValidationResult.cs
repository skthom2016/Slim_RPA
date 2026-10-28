namespace Tracto.Designer.Core.IsolatedMode;

public sealed class IsolatedValidationResult
{
    public bool IsCompliant { get; }
    public IReadOnlyList<IsolatedViolation> Violations { get; }

    public IsolatedValidationResult(IEnumerable<IsolatedViolation> violations)
    {
        Violations = [..violations];
        IsCompliant = Violations.Count == 0;
    }

    public static IsolatedValidationResult Compliant() => new([]);
}
