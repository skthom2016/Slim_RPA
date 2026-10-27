#nullable enable

using Tracto.Contract.Models.Envelope;

namespace Tracto.Contract.Validation;

public sealed class PolicyEnforcer : IPolicyEnforcer
{
    private readonly PolicyRegistry _registry;

    public PolicyEnforcer(PolicyRegistry registry) => _registry = registry;

    public ValidationResult EnforcePolicies(AutomationEnvelope envelope)
    {
        if (envelope.PolicyRefs is null || envelope.PolicyRefs.Count == 0)
            return ValidationResult.Ok(); // No policy refs declared — permitted

        var diagnostics = new List<ValidationDiagnostic>();
        foreach (var policyRef in envelope.PolicyRefs)
        {
            if (!_registry.Contains(policyRef))
                diagnostics.Add(new ValidationDiagnostic(
                    ValidationSeverity.Error,
                    "UNREGISTERED_POLICY_REF",
                    $"Policy reference '{policyRef}' is not in the approved policy registry. " +
                    $"Registered refs: [{string.Join(", ", _registry.GetAllRefs())}].",
                    "policyRefs",
                    policyRef));
        }
        return diagnostics.Count == 0 ? ValidationResult.Ok() : ValidationResult.Fail(diagnostics);
    }

    public bool IsPolicyRegistered(string policyRef) => _registry.Contains(policyRef);
    public IReadOnlyList<string> GetRegisteredPolicies() => _registry.GetAllRefs();
}
