#nullable enable

using System.Threading;
using System.Threading.Tasks;
using Tracto.Contract.Models.Envelope;

namespace Tracto.Contract.Validation;

public interface IEnvelopeValidator
{
    // Validates a JSON string representing an AutomationEnvelope against schema + semantic rules.
    // Returns ValidationResult with all diagnostics (not just the first).
    Task<ValidationResult> ValidateAsync(string envelopeJson, CancellationToken ct = default);

    ValidationResult ValidateObject(AutomationEnvelope envelope);
}
