namespace Tracto.TerminalRuntime.Domain.Service;

/// <summary>
/// Immutable value object returned by a service.invoke call.
///
/// CEC v1.0.1 invariant #6: the runtime branches only on <see cref="Outcome"/>
/// (or an approved <see cref="ActionCode"/>), never on domain-specific fields
/// such as amounts, customer identifiers, or order details.
///
/// CEC v1.0.1 invariant #4: no plaintext secrets may appear in any field of this record.
/// </summary>
/// <param name="Outcome">
///   One of the five canonical service-outcome categories.
/// </param>
/// <param name="ActionCode">
///   Optional approved action code returned by the service.
///   The runtime may only branch on statically configured, approved action codes.
///   Dynamic or unrecognised codes must be ignored.
/// </param>
/// <param name="BusinessOutcome">
///   Opaque, Orchestrator-facing business outcome label.
///   Set to "REJECTED" by the runtime when <see cref="Outcome"/> is
///   <see cref="ServiceOutcome.BusinessRejection"/>.
///   Null for all other outcomes.
///   Must not contain plaintext secrets (CEC invariant #4).
/// </param>
public sealed record ServiceInvokeResult(
    ServiceOutcome Outcome,
    string?        ActionCode,
    string?        BusinessOutcome);
