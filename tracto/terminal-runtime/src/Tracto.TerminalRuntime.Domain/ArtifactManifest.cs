namespace Tracto.TerminalRuntime.Domain;

/// <summary>
/// Immutable descriptor of a deployable artifact submitted to the Terminal Runtime.
/// Domain layer — zero framework dependencies.
/// </summary>
/// <param name="RuntimeType">
///   Must equal <c>"terminal"</c> for this worker.  Any other value (e.g. <c>"browser"</c>)
///   causes the artifact to be rejected before any shell interaction.
/// </param>
/// <param name="SchemaMajor">
///   Major version of the command schema.  Only major version 1 is accepted.
/// </param>
/// <param name="ActionRefs">
///   Fully-qualified action identifiers referenced by the artifact
///   (e.g. <c>terminal.run-script</c>, <c>browser.navigate</c>).
/// </param>
/// <param name="CommandRefs">
///   Raw command template strings that the artifact intends to execute.
///   These are inspected for business-arithmetic predicates and dynamic
///   shell interpolation from API results.
/// </param>
/// <param name="Signature">
///   HMAC-SHA256 hex-string over the canonical artifact payload, signed with
///   the pre-shared key held in <c>ArtifactSignatureSettings</c>.
/// </param>
public sealed record ArtifactManifest(
    string RuntimeType,
    int SchemaMajor,
    string[] ActionRefs,
    string[] CommandRefs,
    string Signature);
