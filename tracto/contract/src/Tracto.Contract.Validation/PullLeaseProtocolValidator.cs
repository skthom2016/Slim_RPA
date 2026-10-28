#nullable enable

using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Tracto.Contract.Validation;

/// <summary>
/// Validates pull-lease protocol message sequencing and semantics.
/// Implements C4 approved rules for worker registration integrity
/// and C3 approved rules for UNKNOWN outcome handling.
/// </summary>
public sealed class PullLeaseProtocolValidator
{
    private readonly ISchemaValidator _schemaValidator;

    public PullLeaseProtocolValidator(ISchemaValidator schemaValidator)
        => _schemaValidator = schemaValidator;

    /// <summary>
    /// Validates a single protocol message JSON against the pull-lease-protocol schema.
    /// </summary>
    public async Task<ValidationResult> ValidateMessageAsync(string messageJson, CancellationToken ct = default)
        => await _schemaValidator.ValidateAsync(messageJson, "pull-lease-protocol.schema.json", ct);

    /// <summary>
    /// Validates the sequence: a STEP_RESULT message carrying UNKNOWN run state must set needsAttention=true.
    /// C3 rule: UNKNOWN outcomes require needsAttention.
    /// REF-09: lastSeenAttemptId must be present in all pull messages for reconciliation.
    /// </summary>
    public ValidationResult ValidateStepResultSemantics(JsonElement stepResultMessage)
    {
        var diagnostics = new List<ValidationDiagnostic>();

        if (!stepResultMessage.TryGetProperty("messageType", out var msgType)
            || msgType.GetString() != "STEP_RESULT")
            return ValidationResult.Ok();

        if (!stepResultMessage.TryGetProperty("inlineResult", out var result)) return ValidationResult.Ok();

        // Check: if runState=UNKNOWN, needsAttention must be true
        if (result.TryGetProperty("runState", out var runState)
            && runState.GetString() == "UNKNOWN")
        {
            bool hasAttention = result.TryGetProperty("needsAttention", out var na) && na.GetBoolean();
            if (!hasAttention)
                diagnostics.Add(new ValidationDiagnostic(ValidationSeverity.Error, "UNKNOWN_MISSING_NEEDS_ATTENTION",
                    "A STEP_RESULT with runState=UNKNOWN must set needsAttention=true (C3 approved rule 4).",
                    "inlineResult.needsAttention"));
        }

        // Check: UNKNOWN must include unknownReason
        if (result.TryGetProperty("runState", out var rs2) && rs2.GetString() == "UNKNOWN")
        {
            bool hasReason = result.TryGetProperty("unknownReason", out var ur)
                && ur.ValueKind == JsonValueKind.String
                && !string.IsNullOrEmpty(ur.GetString());
            if (!hasReason)
                diagnostics.Add(new ValidationDiagnostic(ValidationSeverity.Error, "UNKNOWN_MISSING_REASON",
                    "A STEP_RESULT with runState=UNKNOWN must include a non-empty unknownReason string (C3 approved rule 5).",
                    "inlineResult.unknownReason"));
        }

        // REF-09: lastSeenAttemptId must be present in all pull messages for reconciliation
        if (!stepResultMessage.TryGetProperty("lastSeenAttemptId", out var lastSeen)
            || lastSeen.ValueKind == JsonValueKind.Null
            || string.IsNullOrEmpty(lastSeen.GetString()))
            diagnostics.Add(new ValidationDiagnostic(ValidationSeverity.Warning, "MISSING_LAST_SEEN_ATTEMPT_ID",
                "lastSeenAttemptId should be present in STEP_RESULT messages for reconciliation (REF-09).",
                "lastSeenAttemptId"));

        return diagnostics.Count == 0 ? ValidationResult.Ok() : ValidationResult.Fail(diagnostics);
    }
}
