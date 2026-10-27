#nullable enable

using System.Threading;
using System.Threading.Tasks;

namespace Tracto.Contract.Validation;

public interface ISchemaValidator
{
    Task<ValidationResult> ValidateAsync(string json, string schemaFileName, CancellationToken ct = default);
    Task<ValidationResult> ValidateEnvelopeAsync(string json, CancellationToken ct = default);
    Task<ValidationResult> ValidateWorkerRegistrationAsync(string json, CancellationToken ct = default);
}
