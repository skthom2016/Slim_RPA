using Tracto.Contract.Models.Protocol;

namespace Tracto.Contract.Validation;

/// <summary>
/// Validates a WorkerRegistration against the C4-approved behavioural rules.
/// Silent acceptance is prohibited: invalid registrations must be rejected with errorCategory=VALIDATION.
/// </summary>
public interface IWorkerRegistrationValidator
{
    /// <summary>
    /// Validates a JSON-serialized WorkerRegistration string.
    /// Performs JSON schema validation followed by behavioural rule evaluation.
    /// </summary>
    Task<ValidationResult> ValidateAsync(string registrationJson, CancellationToken ct = default);

    /// <summary>
    /// Validates a deserialized WorkerRegistration object against all C4 behavioural rules.
    /// Skips schema validation — caller must ensure the object was schema-validated first.
    /// </summary>
    ValidationResult ValidateObject(WorkerRegistration registration);
}
