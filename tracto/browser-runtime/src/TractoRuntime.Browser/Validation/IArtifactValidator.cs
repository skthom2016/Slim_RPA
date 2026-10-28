namespace TractoRuntime.Browser.Validation;

/// <summary>
/// Validates a signed artifact envelope before any browser action is taken.
/// </summary>
/// <remarks>
/// All five checks (signature, runtimeType, schema major, mixed-engine, and closed-behavior)
/// must pass; the first failure returns immediately and the run is terminated with the
/// appropriate CEC §6.2 <see cref="TractoRuntime.Browser.Contracts.ErrorCategory"/>.
/// Authority: CEC-001, CEC-002, CEC-012.
/// </remarks>
public interface IArtifactValidator
{
    /// <summary>
    /// Validates the given <paramref name="envelope"/>.
    /// The result is synchronous because all checks are structural and require no I/O.
    /// </summary>
    /// <param name="envelope">The signed artifact envelope to validate.</param>
    /// <returns>
    /// A <see cref="ArtifactValidationResult"/> that is valid when all checks pass,
    /// or invalid with a category and detail code at the first failure.
    /// </returns>
    ArtifactValidationResult Validate(ArtifactEnvelope envelope);
}
