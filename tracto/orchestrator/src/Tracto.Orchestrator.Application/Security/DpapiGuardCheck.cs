#nullable enable
namespace Tracto.Orchestrator.Application.Security;

/// <summary>
/// Code-convention guard ensuring the Windows DPAPI class is never referenced
/// anywhere in the <c>src/</c> tree.
///
/// Domain invariant 4: "No plaintext secrets stored or returned.
/// Orchestrator never calls DPAPI."
///
/// The actual enforcement is done by the unit-test companion
/// <c>DpapiGuardCheckTests</c> which greps the source tree and asserts zero matches.
/// This class exists so the Application layer has a typed artefact to document
/// and reference the policy, and so the test has a stable surface to couple to.
/// </summary>
public static class DpapiGuardCheck
{
    /// <summary>
    /// The exact identifier that must not appear in any <c>src/</c> file.
    /// Built at runtime via string concatenation so this file does not itself
    /// trigger the guard check that scans for the forbidden identifier.
    /// The test always uses this property, so a rename here propagates automatically.
    /// </summary>
    public static readonly string ForbiddenIdentifier = "Protected" + "Data";

    /// <summary>
    /// Returns the set of file extensions that the guard check must inspect
    /// under <c>src/</c>.
    /// </summary>
    public static IReadOnlySet<string> InspectedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".csx", ".props", ".targets"
        };
}
