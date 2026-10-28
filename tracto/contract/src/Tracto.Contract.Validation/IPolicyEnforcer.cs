#nullable enable

using System.Collections.Generic;
using Tracto.Contract.Models.Envelope;

namespace Tracto.Contract.Validation;

public interface IPolicyEnforcer
{
    // Returns diagnostics for any policyRef in the envelope not found in the registry.
    ValidationResult EnforcePolicies(AutomationEnvelope envelope);

    bool IsPolicyRegistered(string policyRef);

    IReadOnlyList<string> GetRegisteredPolicies();
}
