using System.Collections.Frozen;
using TractoRuntime.Browser.Assertion;
using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Validation;

/// <summary>
/// Default implementation of <see cref="IArtifactValidator"/>.
/// </summary>
/// <remarks>
/// Checks are executed in the order mandated by CEC §2.1 §3:
/// <list type="number">
///   <item>Signature / digest presence (CEC-001) — unsigned artifact</item>
///   <item>RuntimeType equality (CEC-012) — wrong-engine artifact</item>
///   <item>Schema major version (CEC-007) — unsupported schema</item>
///   <item>Mixed-engine action references (CEC-002) — desktop.* + browser.*</item>
///   <item>Closed-behavior forbidden actions (CEC-002) — page.evaluate, APIRequest, etc.</item>
///   <item>CSS selector policy (CEC-001 / BRW-005) — css without allowCss policy</item>
/// </list>
/// The first failing check returns immediately; no browser is launched.
/// </remarks>
public sealed class ArtifactValidator : IArtifactValidator
{
    private const string ExpectedRuntimeType = "browser";
    private const string SupportedSchemaMajor = "1";

    /// <summary>
    /// Exact action names that violate CEC-002 closed-behavior rules.
    /// Business-arithmetic predicates, dynamically selected scripts, and page.evaluate
    /// expressions are not permitted at schema validation time.
    /// </summary>
    private static readonly FrozenSet<string> ForbiddenActions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "browser.evaluate",
            "browser.evaluate.script",
            "page.evaluate",
            "page.evaluate.handle",
            "browser.api.request",
            "browser.apirequest",
            "browser.fetch"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public ArtifactValidationResult Validate(ArtifactEnvelope envelope)
    {
        var signatureResult = ValidateSignature(envelope);
        if (!signatureResult.IsValid) return signatureResult;

        var runtimeTypeResult = ValidateRuntimeType(envelope);
        if (!runtimeTypeResult.IsValid) return runtimeTypeResult;

        var schemaMajorResult = ValidateSchemaMajor(envelope);
        if (!schemaMajorResult.IsValid) return schemaMajorResult;

        var mixedEngineResult = ValidateMixedEngineActions(envelope);
        if (!mixedEngineResult.IsValid) return mixedEngineResult;

        var closedBehaviorResult = ValidateClosedBehavior(envelope);
        if (!closedBehaviorResult.IsValid) return closedBehaviorResult;

        var cssPolicyResult = ValidateCssSelectorPolicy(envelope);
        if (!cssPolicyResult.IsValid) return cssPolicyResult;

        // BRW-US-010 (DOM-008): reject business predicates before any browser interaction.
        var assertionResult = ValidateAssertionPredicates(envelope);
        if (!assertionResult.IsValid) return assertionResult;

        return ArtifactValidationResult.Valid();
    }

    /// <summary>
    /// CEC-001: An unsigned artifact is rejected before any browser is launched.
    /// Both the cryptographic signature and the artifact digest must be present and non-empty.
    /// </summary>
    private static ArtifactValidationResult ValidateSignature(ArtifactEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.Signature) ||
            string.IsNullOrWhiteSpace(envelope.ArtifactDigest))
        {
            return ArtifactValidationResult.Invalid(
                ErrorCategory.VALIDATION,
                "Artifact is unsigned or its digest is missing. " +
                "Unsigned artifacts are rejected before any browser action.",
                "browser.artifactUnsigned");
        }

        return ArtifactValidationResult.Valid();
    }

    /// <summary>
    /// CEC-012: An artifact whose runtimeType is not "browser" is rejected before any browser interaction.
    /// A "desktop" artifact submitted to a Browser worker is explicitly rejected here.
    /// </summary>
    private static ArtifactValidationResult ValidateRuntimeType(ArtifactEnvelope envelope)
    {
        if (!string.Equals(envelope.RuntimeType, ExpectedRuntimeType, StringComparison.OrdinalIgnoreCase))
        {
            return ArtifactValidationResult.Invalid(
                ErrorCategory.VALIDATION,
                $"Artifact runtimeType '{envelope.RuntimeType}' is not accepted by the Browser Runtime. " +
                $"Only runtimeType='{ExpectedRuntimeType}' is valid.",
                "browser.artifactTypeMismatch");
        }

        return ArtifactValidationResult.Valid();
    }

    /// <summary>
    /// CEC-007: Only script schema major version "1" is accepted.
    /// An artifact referencing any other major version is rejected.
    /// </summary>
    private static ArtifactValidationResult ValidateSchemaMajor(ArtifactEnvelope envelope)
    {
        if (!string.Equals(envelope.SchemaMajor, SupportedSchemaMajor, StringComparison.Ordinal))
        {
            return ArtifactValidationResult.Invalid(
                ErrorCategory.VALIDATION,
                $"Artifact schema major '{envelope.SchemaMajor}' is not supported. " +
                $"Only schema major '{SupportedSchemaMajor}' is accepted.",
                "browser.schemaMajorUnsupported");
        }

        return ArtifactValidationResult.Valid();
    }

    /// <summary>
    /// CEC-002: An artifact containing mixed desktop.* and browser.* action references
    /// is rejected at schema validation time before any side effect occurs.
    /// An artifact containing only desktop.* actions is also rejected (wrong engine).
    /// </summary>
    private static ArtifactValidationResult ValidateMixedEngineActions(ArtifactEnvelope envelope)
    {
        bool hasDesktopActions = envelope.Script.Steps.Any(
            s => s.Action.StartsWith("desktop.", StringComparison.OrdinalIgnoreCase));

        bool hasBrowserActions = envelope.Script.Steps.Any(
            s => s.Action.StartsWith("browser.", StringComparison.OrdinalIgnoreCase));

        if (hasDesktopActions && hasBrowserActions)
        {
            return ArtifactValidationResult.Invalid(
                ErrorCategory.VALIDATION,
                "Artifact contains mixed desktop.* and browser.* action references. " +
                "Cross-engine scripts are rejected at schema validation time.",
                "browser.mixedEngineScript");
        }

        if (hasDesktopActions)
        {
            return ArtifactValidationResult.Invalid(
                ErrorCategory.VALIDATION,
                "Artifact contains desktop.* action references and cannot be executed by the Browser Runtime.",
                "browser.mixedEngineScript");
        }

        return ArtifactValidationResult.Valid();
    }

    /// <summary>
    /// CEC-002: Business-arithmetic predicates, dynamically selected scripts, and
    /// page.evaluate / APIRequest expressions are rejected at schema validation time,
    /// before any browser side effect.
    /// Authority: CEC-002, CEC-011, CEC-HOST-009; Appendix B (Browser traffic distinction).
    /// </summary>
    private static ArtifactValidationResult ValidateClosedBehavior(ArtifactEnvelope envelope)
    {
        foreach (var step in envelope.Script.Steps)
        {
            if (ForbiddenActions.Contains(step.Action))
            {
                return ArtifactValidationResult.Invalid(
                    ErrorCategory.POLICY_DENIED,
                    $"Step '{step.StepId}' uses forbidden action '{step.Action}'. " +
                    "Business-arithmetic predicates, dynamically selected scripts, and " +
                    "page.evaluate / APIRequest expressions are rejected before any browser side effect.",
                    "browser.evaluationDenied");
            }

            // Catch partial-match patterns not covered by the exact set above.
            if (step.Action.Contains("apirequest", StringComparison.OrdinalIgnoreCase) ||
                step.Action.Contains("api.request", StringComparison.OrdinalIgnoreCase))
            {
                return ArtifactValidationResult.Invalid(
                    ErrorCategory.POLICY_DENIED,
                    $"Step '{step.StepId}' action '{step.Action}' targets a direct API request path. " +
                    "Direct HTTP/API calls outside browser UI interaction are not permitted.",
                    "browser.evaluationDenied");
            }
        }

        return ArtifactValidationResult.Valid();
    }

    /// <summary>
    /// CEC-001 / BRW-005: A selectorRef that specifies a CSS selector without
    /// allowCss=true in the deployment policy must be rejected at artifact validation time,
    /// before any browser interaction (spec §9, normative selector schema reference F-08).
    /// </summary>
    private static ArtifactValidationResult ValidateCssSelectorPolicy(ArtifactEnvelope envelope)
    {
        if (envelope.AllowCss) return ArtifactValidationResult.Valid();

        foreach (var step in envelope.Script.Steps)
        {
            if (step.Selector?.Css is not null)
            {
                return ArtifactValidationResult.Invalid(
                    ErrorCategory.VALIDATION,
                    $"Step '{step.StepId}' uses a CSS selector but the deployment policy does not declare " +
                    "allowCss=true. CSS selectors must be explicitly policy-allowed before use.",
                    "browser.cssNotAllowed");
            }
        }

        return ArtifactValidationResult.Valid();
    }

    /// <summary>
    /// DOM-008 / BRW-US-010 AC#2 / BRW-AT-14:
    /// For each <c>browser.page.assert</c> step, validate that the declared
    /// <see cref="AssertionSpec"/> is a permitted technical predicate.
    /// Business-arithmetic predicates (e.g. <c>price &gt; 100</c>) and raw expressions are
    /// rejected before any browser interaction.
    /// </summary>
    private static ArtifactValidationResult ValidateAssertionPredicates(ArtifactEnvelope envelope)
    {
        var parser = new ConditionParser();

        foreach (var step in envelope.Script.Steps)
        {
            if (!step.Action.Equals("browser.page.assert", StringComparison.OrdinalIgnoreCase))
                continue;

            // A browser.page.assert step with no AssertionSpec is malformed.
            if (step.Assertion is null)
            {
                return ArtifactValidationResult.Invalid(
                    ErrorCategory.VALIDATION,
                    $"Step '{step.StepId}' declares action 'browser.page.assert' but has no AssertionSpec. " +
                    "Each assert step must declare a technical predicate.",
                    "browser.selectorRefInvalid");
            }

            // Validate the predicate — business/arithmetic expressions are rejected here.
            var parseResult = parser.Validate(step.Assertion);
            if (!parseResult.IsValid)
            {
                return ArtifactValidationResult.Invalid(
                    ErrorCategory.VALIDATION,
                    $"Step '{step.StepId}' assertion rejected: {parseResult.RejectionReason}",
                    parseResult.DetailCode ?? "browser.selectorRefInvalid");
            }
        }

        return ArtifactValidationResult.Valid();
    }
}
