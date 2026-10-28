using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracto.TerminalRuntime.Domain.Cleanup;
using Tracto.TerminalRuntime.Infrastructure.Cleanup;

namespace Tracto.TerminalRuntime.Unit.Tests.Cleanup;

/// <summary>
/// Unit tests for TER-US-019 AC-1 and AC-2: filesystem cleanup.
///
/// AC-1: On cancel, owned temporary files within approved roots are removed;
///       approved result files and diagnostics are never touched.
/// AC-2: Files outside approved roots must never be deleted, even when listed
///       in OwnedFiles.
/// </summary>
public sealed class FilesystemCleanupTests : IDisposable
{
    private readonly string _approvedRoot;
    private readonly string _otherRoot;
    private readonly FilesystemCleanup _sut;

    public FilesystemCleanupTests()
    {
        _approvedRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        _otherRoot    = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_approvedRoot);
        Directory.CreateDirectory(_otherRoot);

        _sut = new FilesystemCleanup(NullLogger<FilesystemCleanup>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_approvedRoot)) Directory.Delete(_approvedRoot, recursive: true);
        if (Directory.Exists(_otherRoot))    Directory.Delete(_otherRoot,    recursive: true);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private string CreateTempFile(string root, string name)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, "temp content");
        return path;
    }

    // ── AC-1: owned temp files within approved root are removed ─────────────

    [Fact]
    public async Task CleanupAsync_OwnedFilesInsideApprovedRoot_AreDeleted()
    {
        // Arrange
        var file1 = CreateTempFile(_approvedRoot, "tmp_a.tmp");
        var file2 = CreateTempFile(_approvedRoot, "tmp_b.tmp");

        var registry = new TempFileRegistry(
            OwnedFiles:    [file1, file2],
            ApprovedRoots: [_approvedRoot]);

        // Act
        var result = await _sut.CleanupAsync(registry);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.FilesDeleted.Should().Be(2);
        result.Value.FilesSkippedOutsideRoot.Should().Be(0);
        File.Exists(file1).Should().BeFalse("temp file must be removed on cancel");
        File.Exists(file2).Should().BeFalse("temp file must be removed on cancel");
    }

    [Fact]
    public async Task CleanupAsync_WhenFileAlreadyGone_CountsDeletedZero()
    {
        // Arrange — file does not exist (already removed or never created)
        var missingFile = Path.Combine(_approvedRoot, "already_gone.tmp");

        var registry = new TempFileRegistry(
            OwnedFiles:    [missingFile],
            ApprovedRoots: [_approvedRoot]);

        // Act
        var result = await _sut.CleanupAsync(registry);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.FilesDeleted.Should().Be(0, "non-existent files are silently skipped");
        result.Value.FilesSkippedOutsideRoot.Should().Be(0);
    }

    [Fact]
    public async Task CleanupAsync_ApprovedResultFileNotInOwnedFiles_IsNeverTouched()
    {
        // Arrange — result file exists in the root but is NOT in OwnedFiles
        var resultFile = CreateTempFile(_approvedRoot, "output_result.json");
        var tempFile   = CreateTempFile(_approvedRoot, "tmp_c.tmp");

        var registry = new TempFileRegistry(
            OwnedFiles:    [tempFile],           // only the temp file is owned
            ApprovedRoots: [_approvedRoot]);

        // Act
        await _sut.CleanupAsync(registry);

        // Assert — result file must still exist
        File.Exists(resultFile).Should().BeTrue("approved result files must not be removed");
        File.Exists(tempFile).Should().BeFalse("owned temp file must be removed");
    }

    // ── AC-2: files outside approved roots must NOT be deleted ──────────────

    [Fact]
    public async Task CleanupAsync_OwnedFileOutsideApprovedRoot_IsNotDeleted()
    {
        // Arrange — file lives in _otherRoot which is NOT an approved root
        var fileOutside = CreateTempFile(_otherRoot, "secret.tmp");

        var registry = new TempFileRegistry(
            OwnedFiles:    [fileOutside],
            ApprovedRoots: [_approvedRoot]);   // only _approvedRoot is approved

        // Act
        var result = await _sut.CleanupAsync(registry);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.FilesDeleted.Should().Be(0, "file is outside approved roots and must be skipped");
        result.Value.FilesSkippedOutsideRoot.Should().Be(1);
        File.Exists(fileOutside).Should().BeTrue("files outside approved roots must never be deleted");
    }

    [Fact]
    public async Task CleanupAsync_PathTraversalAttempt_DoesNotEscapeApprovedRoot()
    {
        // Arrange — a crafted path that traverses above the approved root
        var traversalPath = Path.Combine(_approvedRoot, "..", "secret_outside.tmp");
        // Create the file at the actual resolved location
        var resolvedPath = Path.GetFullPath(traversalPath);
        if (!File.Exists(resolvedPath))
            File.WriteAllText(resolvedPath, "sensitive");

        var registry = new TempFileRegistry(
            OwnedFiles:    [traversalPath],
            ApprovedRoots: [_approvedRoot]);

        try
        {
            // Act
            var result = await _sut.CleanupAsync(registry);

            // Assert — the traversal path resolves outside _approvedRoot so it must be skipped
            result.IsSuccess.Should().BeTrue();
            result.Value.FilesDeleted.Should().Be(0,
                "a traversal path that escapes the approved root must be refused");
            result.Value.FilesSkippedOutsideRoot.Should().Be(1);
        }
        finally
        {
            if (File.Exists(resolvedPath)) File.Delete(resolvedPath);
        }
    }

    [Fact]
    public async Task CleanupAsync_EmptyRegistry_ReturnsZeroCounts()
    {
        var registry = new TempFileRegistry(
            OwnedFiles:    [],
            ApprovedRoots: [_approvedRoot]);

        var result = await _sut.CleanupAsync(registry);

        result.IsSuccess.Should().BeTrue();
        result.Value.FilesDeleted.Should().Be(0);
        result.Value.FilesSkippedOutsideRoot.Should().Be(0);
    }
}
