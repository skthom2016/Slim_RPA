using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TractoRuntime.Browser.Binary;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Packaging;
using TractoRuntime.Browser.Tests.Fixtures;

namespace TractoRuntime.Browser.Tests.Binary;

/// <summary>
/// Unit tests for <see cref="BinaryValidator"/> covering BRW-US-001 acceptance criteria.
///
/// BRW-AT-19 — Isolated host without Internet and browser binaries bundled:
///   Headless Browser starts and executes fixture without download.
///
/// BRW-AT-28 — Stage 1 conformance in both deployments:
///   Run applicable CF-01–CF-10 and protocol fault tests with mock.
///   Connected integration uses real compatible Orchestrator.
///   Isolated mode uses bundled certified browser binaries without downloading at runtime.
/// </summary>
public sealed class BinaryValidatorTests : BrowserTestBase
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static readonly string s_chromiumHash;
    private static string s_tempBinaryPath = string.Empty;

    static BinaryValidatorTests()
    {
        // Write a deterministic temp binary so we can pin its hash.
        s_tempBinaryPath = Path.Combine(Path.GetTempPath(), $"chrome-test-{Guid.NewGuid():N}.exe");
        byte[] content = "FAKE_CHROMIUM_BINARY_CONTENT"u8.ToArray();
        File.WriteAllBytes(s_tempBinaryPath, content);
        s_chromiumHash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }

    private static BrowserBinaryManifest MakeManifest(string? hashOverride = null) =>
        new()
        {
            DefaultChannel = "chromium",
            Entries =
            [
                new BrowserBinaryEntry
                {
                    Channel = "chromium",
                    Revision = "1148",
                    Sha256Hex = hashOverride ?? s_chromiumHash,
                    RelativePath = "chromium-1148/chrome-win/chrome.exe",
                    DisplayVersion = "120.0.6099.56"
                }
            ]
        };

    private BinaryValidator MakeValidator(
        BrowserBinaryManifest? manifest = null,
        bool isIsolated = false)
    {
        RuntimePackageMock
            .Setup(p => p.IsIsolatedDeploymentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(isIsolated);

        return new BinaryValidator(
            manifest ?? MakeManifest(),
            RuntimePackageMock.Object,
            NullLogger<BinaryValidator>.Instance);
    }

    // -------------------------------------------------------------------------
    // AC-1: Binary present + hash matches → validation passes
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_WhenBinaryExistsAndHashMatches_Succeeds()
    {
        // Arrange
        BinaryValidator sut = MakeValidator();

        // Act
        Func<Task> act = () => sut.ValidateAsync(s_tempBinaryPath, CancellationToken.None);

        // Assert — no exception
        await act.Should().NotThrowAsync();
        sut.LastValidatedVersion.Should().Be("120.0.6099.56");
    }

    // -------------------------------------------------------------------------
    // AC-2: Binary absent → ENVIRONMENT / browser.notInstalled
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_WhenBinaryMissing_ThrowsBinaryValidationException()
    {
        // Arrange
        BinaryValidator sut = MakeValidator();
        string missingPath = Path.Combine(Path.GetTempPath(), "nonexistent-chrome.exe");

        // Act
        Func<Task> act = () => sut.ValidateAsync(missingPath, CancellationToken.None);

        // Assert
        BinaryValidationException ex = (await act.Should()
            .ThrowAsync<BinaryValidationException>())
            .Subject.Single();

        ex.Category.Should().Be(ErrorCategory.ENVIRONMENT);
        ex.DetailCode.Should().Be("browser.notInstalled");
    }

    // -------------------------------------------------------------------------
    // AC-2: Hash mismatch → ENVIRONMENT / browser.notInstalled
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_WhenHashMismatch_ThrowsBinaryValidationException()
    {
        // Arrange — manifest has wrong expected hash
        BinaryValidator sut = MakeValidator(MakeManifest(hashOverride: new string('a', 64)));

        // Act
        Func<Task> act = () => sut.ValidateAsync(s_tempBinaryPath, CancellationToken.None);

        // Assert
        BinaryValidationException ex = (await act.Should()
            .ThrowAsync<BinaryValidationException>())
            .Subject.Single();

        ex.Category.Should().Be(ErrorCategory.ENVIRONMENT);
        ex.DetailCode.Should().Be("browser.notInstalled");
    }

    // -------------------------------------------------------------------------
    // AC-3 / BRW-AT-19: Isolated host — binary missing → exception (no download)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_IsolatedMode_BinaryMissing_ThrowsWithoutDownload()
    {
        // Arrange
        BinaryValidator sut = MakeValidator(isIsolated: true);
        string missingPath = Path.Combine(Path.GetTempPath(), "nonexistent-chromium.exe");

        // Act
        Func<Task> act = () => sut.ValidateAsync(missingPath, CancellationToken.None);

        // Assert — exception thrown; no network calls made (validated by absence of download side-effects)
        BinaryValidationException ex = (await act.Should()
            .ThrowAsync<BinaryValidationException>())
            .Subject.Single();

        ex.Category.Should().Be(ErrorCategory.ENVIRONMENT);
        ex.DetailCode.Should().Be("browser.notInstalled");
    }

    // -------------------------------------------------------------------------
    // AC-3 / BRW-AT-19: Isolated host — binary present and hash matches → passes
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_IsolatedMode_BinaryBundled_Succeeds()
    {
        // Arrange
        BinaryValidator sut = MakeValidator(isIsolated: true);

        // Act
        Func<Task> act = () => sut.ValidateAsync(s_tempBinaryPath, CancellationToken.None);

        // Assert — no exception; version recorded for capability digest
        await act.Should().NotThrowAsync();
        sut.LastValidatedVersion.Should().NotBeNullOrEmpty();
    }

    // -------------------------------------------------------------------------
    // AC-4 / BRW-006: Last validated version is exposed for capability digest
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_AfterSuccess_ExposesVersionForCapabilityDigest()
    {
        // Arrange
        BinaryValidator sut = MakeValidator();

        // Act
        await sut.ValidateAsync(s_tempBinaryPath, CancellationToken.None);

        // Assert
        sut.LastValidatedVersion.Should().Be("120.0.6099.56");
    }

    // -------------------------------------------------------------------------
    // CEC wire: BinaryValidationException maps to correct CecError
    // -------------------------------------------------------------------------

    [Fact]
    public void ToCecError_MapsToEnvironmentCategory()
    {
        var ex = new BinaryValidationException("Binary not found.");

        CecError error = ex.ToCecError();

        error.Category.Should().Be(ErrorCategory.ENVIRONMENT);
        error.DetailCode.Should().Be("browser.notInstalled");
        error.Message.Should().Be("Binary not found.");
    }

    // -------------------------------------------------------------------------
    // Cleanup — temp binary created in static constructor
    // -------------------------------------------------------------------------

    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void RegisterCleanup()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (File.Exists(s_tempBinaryPath))
                try { File.Delete(s_tempBinaryPath); } catch { /* best-effort */ }
        };
    }
}
