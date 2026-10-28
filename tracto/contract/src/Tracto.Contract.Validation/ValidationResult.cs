#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace Tracto.Contract.Validation;

public record ValidationResult(bool IsValid, IReadOnlyList<ValidationDiagnostic> Diagnostics)
{
    public static ValidationResult Ok() =>
        new(true, Array.Empty<ValidationDiagnostic>());

    public static ValidationResult Fail(params ValidationDiagnostic[] diagnostics) =>
        new(false, diagnostics);

    public static ValidationResult Fail(IEnumerable<ValidationDiagnostic> diagnostics) =>
        new(false, diagnostics.ToList());
}

public record ValidationDiagnostic(
    ValidationSeverity Severity,
    string Code,
    string Message,
    string? Path = null,
    string? Detail = null);

public enum ValidationSeverity
{
    Error,
    Warning,
}
