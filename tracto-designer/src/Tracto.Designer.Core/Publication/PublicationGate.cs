namespace Tracto.Designer.Core.Publication;

using Tracto.Designer.Core.Signing;
using Tracto.Designer.Core.Validation;

/// <summary>
/// Evaluates the ordered publication gates (validation, optional test, signature).
/// </summary>
/// <param name="validator">Script validator used for Gate 1.</param>
/// <param name="signer">Script signer used for Gate 3.</param>
/// <param name="testRequired">
/// When <c>true</c> (the default), Gate 2 requires a passing test result.
/// When <c>false</c>, testing is optional per deployment policy (DS-16-AC01) and
/// Gate 2 is skipped — the VALIDATED lifecycle state is sufficient to publish.
/// </param>
public sealed class PublicationGate(IScriptValidator validator, IScriptSigner signer, bool testRequired = true)
{
    public PublicationResult? CheckGates(PublicationRequest req)
    {
        // Gate 1: script valid
        var validation = validator.Validate(req.Script);
        if (!validation.IsValid)
            return PublicationResult.Failure($"Script is not valid: {validation.Errors.First(e => e.Severity == ValidationSeverity.Error).Message}");

        // Gate 2: test passed (skipped when deployment policy does not mandate testing)
        if (testRequired && !req.TestResult.Passed)
            return PublicationResult.Failure($"Test run did not pass: {req.TestResult.ErrorMessage ?? "Unknown error"}");

        // Gate 3: signature valid
        if (!signer.Verify(req.Script, req.Signature))
            return PublicationResult.Failure("Signature is invalid or does not match the current script. Re-sign before publishing.");

        return null; // all gates pass
    }
}
