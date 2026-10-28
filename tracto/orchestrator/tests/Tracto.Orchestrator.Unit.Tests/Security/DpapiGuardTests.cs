#nullable enable
using FluentAssertions;

namespace Tracto.Orchestrator.Unit.Tests.Security;

/// <summary>
/// OC-17 — Code-convention check: confirms ProtectedData (Windows DPAPI) is not referenced
/// anywhere under src/. Domain invariant 4 forbids any use of Windows-only crypto APIs.
/// </summary>
public sealed class DpapiGuardTests
{
    [Fact]
    public void ProtectedData_IsNotReferencedInSrcDirectory()
    {
        // Arrange — find the repository root by walking up from the test assembly location.
        var assemblyDir  = Path.GetDirectoryName(typeof(DpapiGuardTests).Assembly.Location)!;
        var srcDir       = FindSrcDirectory(assemblyDir);

        srcDir.Should().NotBeNull(
            because: "the test must be able to locate the src/ directory to perform the grep");

        // Act — recursively scan all .cs files under src/ for "ProtectedData".
        var violations = Directory.EnumerateFiles(srcDir!, "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("ProtectedData", StringComparison.Ordinal))
            .ToList();

        // Assert
        violations.Should().BeEmpty(
            because: "domain invariant 4 forbids ProtectedData (Windows DPAPI) in src/; " +
                     $"offending files: {string.Join(", ", violations)}");
    }

    /// <summary>Walks up the directory tree until a folder named "src" is found.</summary>
    private static string? FindSrcDirectory(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
