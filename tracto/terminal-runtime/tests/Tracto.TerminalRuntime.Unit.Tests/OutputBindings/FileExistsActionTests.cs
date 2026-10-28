using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.TerminalRuntime.Application;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Policies;
using Tracto.TerminalRuntime.Infrastructure.OutputBindings;

namespace Tracto.TerminalRuntime.Unit.Tests.OutputBindings;

/// <summary>
/// Unit tests for <see cref="FileExistsAction"/> covering AC-3 and AC-4 of
/// TER-US-014: Output Bindings and Approved Result Artifacts.
/// </summary>
public sealed class FileExistsActionTests
{
    private const string TestRunId     = "run-test-001";
    private const string TestAttemptId = "attempt-test-001";

    private static FileExistsAction CreateSut(IRunJournal? journal = null)
    {
        var mockJournal = journal ?? CreatePassthroughJournalMock().Object;
        return new FileExistsAction(mockJournal, NullLogger<FileExistsAction>.Instance);
    }

    /// <summary>Returns a mock journal whose <c>AppendOutputArtifactRefAsync</c> always succeeds.</summary>
    private static Mock<IRunJournal> CreatePassthroughJournalMock()
    {
        var mock = new Mock<IRunJournal>();
        mock.Setup(j => j.AppendOutputArtifactRefAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok<bool>(true));
        return mock;
    }

    // ── AC-4: path within approved root → boolean result ─────────────────────────

    [Fact]
    public async Task CheckAsync_PathInsideApprovedRoot_FileExists_ReturnsTrue()
    {
        // Arrange — create a real temporary file.
        var root = Path.GetTempPath();
        var policy = new StorageRootPolicy([root]);
        var tempFile = Path.Combine(root, Path.GetRandomFileName());
        File.WriteAllText(tempFile, "tracto-test");

        try
        {
            // Act
            var result = await CreateSut().CheckAsync(tempFile, policy, TestRunId, TestAttemptId);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Exists.Should().BeTrue(
                because: "the file was just created inside the approved root");
            result.Value.Path.Should().Be(Path.GetFullPath(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task CheckAsync_PathInsideApprovedRoot_FileNotExists_ReturnsFalse()
    {
        // Arrange — path inside approved root but file does not exist.
        var root = Path.GetTempPath();
        var policy = new StorageRootPolicy([root]);
        var nonExistentFile = Path.Combine(root, "tracto-nonexistent-" + Guid.NewGuid() + ".txt");

        // Act
        var result = await CreateSut().CheckAsync(nonExistentFile, policy, TestRunId, TestAttemptId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Exists.Should().BeFalse(
            because: "the file does not exist on disk");
    }

    // ── AC-4: path outside approved root → POLICY_DENIED before any stat ─────────

    [Fact]
    public async Task CheckAsync_PathOutsideApprovedRoot_ReturnsPolicyDenied()
    {
        // Arrange — approved root is a specific sub-folder; the path is outside it.
        var approvedRoot = Path.Combine(Path.GetTempPath(), "tracto-approved-" + Guid.NewGuid());
        var policy = new StorageRootPolicy([approvedRoot]);
        var outsidePath = Path.Combine(Path.GetTempPath(), "tracto-outside.txt");

        // Act
        var result = await CreateSut().CheckAsync(outsidePath, policy, TestRunId, TestAttemptId);

        // Assert
        result.IsFailure.Should().BeTrue(
            because: $"'{outsidePath}' is outside the approved root '{approvedRoot}'");
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied,
            because: "AC-4 mandates errorCategory=POLICY_DENIED");
        result.Error.DetailCode.Should().Be("terminal.storageRootDenied");
    }

    [Fact]
    public async Task CheckAsync_PathWithDotDotSegment_ReturnsPolicyDeniedBeforeResolution()
    {
        // Arrange — the raw path contains ".." traversal.
        var root = Path.GetTempPath();
        var policy = new StorageRootPolicy([root]);
        var traversalPath = Path.Combine(root, "subdir", "..", "..", "secret.txt");

        // Act
        var result = await CreateSut().CheckAsync(traversalPath, policy, TestRunId, TestAttemptId);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
        result.Error.DetailCode.Should().Be("terminal.storageRootDenied");
    }

    // ── AC-4: error detail must NOT expose the secret segment ────────────────────

    [Fact]
    public async Task CheckAsync_PathOutsideRoot_ErrorMessageDoesNotContainRawPath()
    {
        // Arrange — the path includes a "secret" segment that must not appear in the error.
        var approvedRoot = Path.Combine(Path.GetTempPath(), "tracto-approved-" + Guid.NewGuid());
        var policy = new StorageRootPolicy([approvedRoot]);
        const string secretSegment = "supersecrettoken12345";
        var sensitvePath = Path.Combine(Path.GetTempPath(), secretSegment, "output.json");

        // Act
        var result = await CreateSut().CheckAsync(sensitvePath, policy, TestRunId, TestAttemptId);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().NotContain(secretSegment,
            because: "AC-4 requires that secret path segments are not exposed in the error detail");
    }

    // ── AC-3: successful check of an existing file → journal evidence written ─────

    [Fact]
    public async Task CheckAsync_ExistingFileInsideRoot_WritesSignedArtifactRefToJournal()
    {
        // Arrange — create a real temporary file.
        var root = Path.GetTempPath();
        var policy = new StorageRootPolicy([root]);
        var tempFile = Path.Combine(root, Path.GetRandomFileName());
        File.WriteAllText(tempFile, "tracto-artifact-evidence-test");

        var journalMock = CreatePassthroughJournalMock();
        var sut = CreateSut(journalMock.Object);

        try
        {
            // Act
            var result = await sut.CheckAsync(tempFile, policy, TestRunId, TestAttemptId);

            // Assert — file check succeeded.
            result.IsSuccess.Should().BeTrue();
            result.Value.Exists.Should().BeTrue();

            // Assert — AC-3: journal was called with runId, attemptId, canonical path, and a non-empty SHA-256 hex.
            // AC-3: verify a signed artifact reference was recorded in the run journal.
            journalMock.Verify(j => j.AppendOutputArtifactRefAsync(
                    TestRunId,
                    TestAttemptId,
                    Path.GetFullPath(tempFile),
                    It.Is<string>(hex => hex.Length == 64 && hex == hex.ToLowerInvariant()),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task CheckAsync_NonExistentFileInsideRoot_DoesNotWriteJournalEntry()
    {
        // Arrange — path inside root but file does not exist (no artifact to hash).
        var root = Path.GetTempPath();
        var policy = new StorageRootPolicy([root]);
        var nonExistentFile = Path.Combine(root, "tracto-missing-" + Guid.NewGuid() + ".txt");

        var journalMock = CreatePassthroughJournalMock();
        var sut = CreateSut(journalMock.Object);

        // Act
        var result = await sut.CheckAsync(nonExistentFile, policy, TestRunId, TestAttemptId);

        // Assert — successful result.
        result.IsSuccess.Should().BeTrue();
        result.Value.Exists.Should().BeFalse();

        // Assert — no journal entry for a non-existent file (no artifact to hash).
        journalMock.Verify(j => j.AppendOutputArtifactRefAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
