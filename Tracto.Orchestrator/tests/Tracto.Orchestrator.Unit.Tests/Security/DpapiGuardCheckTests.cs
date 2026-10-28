#nullable enable
using FluentAssertions;
using Tracto.Orchestrator.Application.Security;

namespace Tracto.Orchestrator.Unit.Tests.Security;

/// <summary>
/// Enforces the DPAPI / ProtectedData prohibition across the entire src/ tree.
///
/// Domain invariant 4:
///   "Orchestrator never calls DPAPI/ProtectedData."
///
/// This test greps every .cs (and related) file under src/ for the string
/// "ProtectedData" and asserts zero matches.  A single match fails the build so
/// the violation is caught before it reaches code review.
///
/// The canonical forbidden identifier is taken from
/// <see cref="DpapiGuardCheck.ForbiddenIdentifier"/> so that a rename of the
/// constant keeps the test correct automatically.
/// </summary>
public sealed class DpapiGuardCheckTests
{
    // Locate src/ relative to the test assembly's location.
    // The test runner places binaries under:
    //   <solution-root>/tests/Tracto.Orchestrator.Unit.Tests/bin/Debug/net10.0/
    // Walking up four directories lands at <solution-root>.
    private static string? LocateSrcDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void Src_ContainsNoProtectedDataReferences()
    {
        // Arrange
        var srcDir = LocateSrcDirectory();
        srcDir.Should().NotBeNull(
            because: "the src/ directory must be discoverable from the test runner output path");

        var forbidden = DpapiGuardCheck.ForbiddenIdentifier; // "ProtectedData"

        var matches = new List<string>();

        foreach (var ext in DpapiGuardCheck.InspectedExtensions)
        {
            var files = Directory.EnumerateFiles(srcDir!, $"*{ext}", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                // Skip generated obj/ directories.
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                var content = File.ReadAllText(file);
                if (content.Contains(forbidden, StringComparison.Ordinal))
                {
                    // Record the relative path for a useful failure message.
                    var rel = Path.GetRelativePath(srcDir!, file);
                    matches.Add(rel);
                }
            }
        }

        matches.Should().BeEmpty(
            because: $"no file in src/ may reference '{forbidden}' (Domain Invariant 4: " +
                     "Orchestrator never calls DPAPI/ProtectedData). " +
                     $"Offending files: {string.Join(", ", matches)}");
    }
}
