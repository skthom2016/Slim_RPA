using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Commands;
using Tracto.TerminalRuntime.Domain.Shells;

namespace Tracto.TerminalRuntime.Application.Commands;

// ---------------------------------------------------------------------------
// Command
// ---------------------------------------------------------------------------

/// <summary>
/// Carries the inputs for command-allowlist and integrity validation (TER-US-006).
/// </summary>
/// <param name="CommandRef">
///   The logical command identifier from the job definition, e.g. "cmd://ping-host".
/// </param>
/// <param name="RequestedShell">
///   The shell the caller intends to use.  Rejected when not in <c>SupportedShells</c>.
/// </param>
public sealed record ValidateCommandRefCommand(
    string CommandRef,
    ShellType RequestedShell);

// ---------------------------------------------------------------------------
// Result payload
// ---------------------------------------------------------------------------

/// <summary>Returned when a command passes all policy and integrity checks.</summary>
/// <param name="Entry">The matched, approved registry entry.</param>
/// <param name="VerifiedPath">
///   Absolute path to the script/binary — guaranteed to have a matching digest.
/// </param>
public sealed record ValidatedCommandRef(
    CommandRegistryEntry Entry,
    string VerifiedPath);

// ---------------------------------------------------------------------------
// Handler
// ---------------------------------------------------------------------------

/// <summary>
/// Validates a command reference against the approved-command registry and verifies
/// the on-disk integrity of the backing script / binary before any process is launched.
///
/// <list type="number">
///   <item>AC-1 — commandRef not in the approved registry →
///         POLICY_DENIED / <c>terminal.commandNotAllowed</c>.</item>
///   <item>AC-2 — script/binary digest does not match the pinned hash →
///         POLICY_DENIED / <c>terminal.commandIntegrityFailure</c>.</item>
///   <item>AC-3 — edited .bat template (digest mismatch) → same path as AC-2.</item>
///   <item>AC-4 — requested shell not in <c>SupportedShells</c> (wrong-runtime) →
///         POLICY_DENIED / <c>terminal.commandNotAllowed</c>.</item>
/// </list>
///
/// No process-launching logic lives here; this is a pure Application-layer handler.
/// </summary>
public sealed class ValidateCommandRefHandler(
    ICommandRegistry registry,
    ICommandHashVerifier hashVerifier)
{
    public Result<ValidatedCommandRef, Error> Handle(ValidateCommandRefCommand command)
    {
        // AC-1: look up registry; reject unknown or explicitly disabled entries.
        var entry = registry.TryGet(command.CommandRef);

        if (entry is null || !entry.IsApproved)
        {
            return Result.Fail<ValidatedCommandRef>(
                Error.PolicyDenied(
                    "CMD_001",
                    $"Command '{command.CommandRef}' is not in the approved allowlist.",
                    detailCode: "terminal.commandNotAllowed"));
        }

        // AC-4: wrong-runtime — reject when the requested shell is not supported.
        if (!entry.SupportedShells.Contains(command.RequestedShell))
        {
            return Result.Fail<ValidatedCommandRef>(
                Error.PolicyDenied(
                    "CMD_002",
                    $"Command '{command.CommandRef}' does not support shell '{command.RequestedShell}'.",
                    detailCode: "terminal.commandNotAllowed"));
        }

        // AC-2 / AC-3: verify on-disk integrity before allowing execution.
        if (!hashVerifier.VerifyFileHash(entry.ExecutablePath, entry.ExpectedSha256))
        {
            return Result.Fail<ValidatedCommandRef>(
                Error.PolicyDenied(
                    "CMD_003",
                    $"Integrity check failed for '{command.CommandRef}': file digest does not match the approved hash.",
                    detailCode: "terminal.commandIntegrityFailure"));
        }

        return Result.Ok(new ValidatedCommandRef(entry, entry.ExecutablePath));
    }
}
