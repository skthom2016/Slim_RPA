using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Validation;

/// <summary>
/// The outcome of a pre-execution artifact validation pass.
/// A failed result carries the CEC §6.2 <see cref="ErrorCategory"/> and a
/// namespaced <c>browser.*</c> <see cref="DetailCode"/> for diagnostics.
/// </summary>
public sealed record ArtifactValidationResult
{
    /// <summary>Whether the artifact passed all validation checks.</summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// CEC §6.2 error category for the first failing check.
    /// <see langword="null"/> when <see cref="IsValid"/> is <see langword="true"/>.
    /// </summary>
    public ErrorCategory? ErrorCategory { get; init; }

    /// <summary>Human-readable rejection reason — must not contain secret values.</summary>
    public string? Message { get; init; }

    /// <summary>
    /// Namespaced <c>browser.*</c> detail code for structured diagnostics.
    /// <see langword="null"/> when <see cref="IsValid"/> is <see langword="true"/>.
    /// </summary>
    public string? DetailCode { get; init; }

    /// <summary>Returns a result indicating the artifact is fully valid.</summary>
    public static ArtifactValidationResult Valid()
        => new() { IsValid = true };

    /// <summary>Returns a result indicating validation failed with the given category and detail.</summary>
    public static ArtifactValidationResult Invalid(
        ErrorCategory category,
        string message,
        string? detailCode = null)
        => new()
        {
            IsValid = false,
            ErrorCategory = category,
            Message = message,
            DetailCode = detailCode
        };
}
