using FluentAssertions;
using Tracto.TerminalRuntime.Domain.Policies;
using Tracto.TerminalRuntime.Infrastructure.Security;

namespace Tracto.TerminalRuntime.Unit.Tests.Policy;

/// <summary>
/// Unit tests for <see cref="PathValidator"/> covering acceptance criteria AC-1 and AC-2
/// of TER-US-003: Working Directory Policy and Environment Variable Handling.
/// </summary>
public sealed class WorkingDirPolicyTests
{
    private readonly PathValidator _sut = new();

    // ── AC-2: Pre-resolution rejection of ".." segments ─────────────────────────

    [Theory]
    [InlineData("../sibling")]
    [InlineData("..\\sibling")]
    [InlineData("foo/../bar")]
    [InlineData("foo\\..\\bar")]
    [InlineData("..")]
    [InlineData("a/b/../../c")]
    public void Validate_DotDotSegment_RejectsBeforeResolution(string cwdRef)
    {
        var policy = new WorkingDirPolicy([Path.GetTempPath()]);

        var result = _sut.Validate(cwdRef, policy);

        result.IsFailure.Should().BeTrue(
            because: $"'{cwdRef}' contains a '..' segment and must be rejected before path resolution");
        result.Error.Kind.Should().Be(Application.Common.ErrorKind.PolicyDenied,
            because: "AC-2 mandates errorCategory=POLICY_DENIED");
        result.Error.DetailCode.Should().Be("terminal.pathTraversalDenied",
            because: "AC-2 mandates detailCode=terminal.pathTraversalDenied");
    }

    // ── AC-1: Post-canonicalization outside-root rejection ───────────────────────

    [Fact]
    public void Validate_PathOutsideApprovedRoot_Rejected()
    {
        // Arrange — approved root is the temp folder; the requested path is the system root
        var approvedRoot = Path.Combine(Path.GetTempPath(), "approved-work");
        var policy = new WorkingDirPolicy([approvedRoot]);
        var outsidePath = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar,
                              Path.AltDirectorySeparatorChar)
                          + "_evil"; // sibling of approved root

        var result = _sut.Validate(outsidePath, policy);

        result.IsFailure.Should().BeTrue(
            because: $"'{outsidePath}' is outside the approved root '{approvedRoot}'");
        result.Error.Kind.Should().Be(Application.Common.ErrorKind.PolicyDenied);
        result.Error.DetailCode.Should().Be("terminal.pathTraversalDenied");
    }

    [Fact]
    public void Validate_EmptyApprovedRoots_RejectsAll()
    {
        var policy = WorkingDirPolicy.Deny; // no approved roots
        var path = Path.GetTempPath();

        var result = _sut.Validate(path, policy);

        result.IsFailure.Should().BeTrue(
            because: "an empty approved-roots list should deny everything");
        result.Error.Kind.Should().Be(Application.Common.ErrorKind.PolicyDenied);
    }

    // ── Happy path ───────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_PathUnderApprovedRoot_ReturnsCanonicalPath()
    {
        // Arrange — use the real temp directory as the approved root
        var approvedRoot = Path.GetTempPath();
        var policy = new WorkingDirPolicy([approvedRoot]);
        // Build a sub-path inside the approved root (no ".." anywhere)
        var subPath = Path.Combine(approvedRoot, "tracto-work", "session-1");

        var result = _sut.Validate(subPath, policy);

        result.IsSuccess.Should().BeTrue(
            because: $"'{subPath}' is inside the approved root '{approvedRoot}'");
        result.Value.Should().Be(Path.GetFullPath(subPath));
    }

    [Fact]
    public void Validate_PathEqualsApprovedRoot_Accepted()
    {
        var approvedRoot = Path.GetTempPath().TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var policy = new WorkingDirPolicy([approvedRoot]);

        var result = _sut.Validate(approvedRoot, policy);

        result.IsSuccess.Should().BeTrue(
            because: "a path that exactly equals an approved root should be accepted");
    }

    [Fact]
    public void Validate_PathMatchesRootPrefix_ButNotSubDirectory_Rejected()
    {
        // "C:\work" must NOT match "C:\workspace" as an approved subtree of "C:\work"
        var approvedRoot = Path.Combine(Path.GetTempPath(), "work");
        var policy = new WorkingDirPolicy([approvedRoot]);
        var sibling = Path.Combine(Path.GetTempPath(), "workspace");

        var result = _sut.Validate(sibling, policy);

        result.IsFailure.Should().BeTrue(
            because: $"'{sibling}' is a sibling of the root '{approvedRoot}', not a subtree");
    }
}
