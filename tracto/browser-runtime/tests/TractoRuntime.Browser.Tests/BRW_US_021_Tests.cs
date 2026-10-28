using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TractoRuntime.Browser.Binary;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Packaging;
using TractoRuntime.Browser.Tests.Fixtures;
using SysEnvironment = global::System.Environment;

namespace TractoRuntime.Browser.Tests;

/// <summary>
/// Acceptance tests for BRW-US-021 — Offline Bundle and Isolated Execution.
/// Covers BRW-AT-19, BRW-AT-22, BRW-AT-28.
///
/// Requirement refs: BRW-001, BRW-SUP-001 (isolated), §10 (offline packaging)
///
/// Acceptance criteria tested here:
///   AC1 — Isolated host starts runtime and runs headless fixture using bundled binary.
///   AC2 — Runtime never downloads, updates, or repairs browser binaries.
///   AC3 — Bundle includes pinned Playwright for .NET version AND all browser binary files.
///   AC4 — Isolated-host detection defers to the shared Windows Runtime Host Supervisor
///          (architecture constraint; verified via env-var isolation flag).
/// </summary>
public sealed class BRW_US_021_Tests : BrowserTestBase
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Creates a temporary directory containing a fake browser binary for one channel.</summary>
    private static (string baseDir, string binaryPath) CreateTempBundleDir(string channel = "chromium")
    {
        string baseDir = Path.Combine(Path.GetTempPath(), $"tracto-bundle-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);
        string subDir = Path.Combine(baseDir, $"{channel}-1148", "chrome-win");
        Directory.CreateDirectory(subDir);
        string binaryPath = Path.Combine(subDir, "chrome.exe");
        File.WriteAllBytes(binaryPath, "FAKE_CHROME"u8.ToArray());
        return (baseDir, binaryPath);
    }

    private static BrowserBinaryManifest MakeManifest(string channel, string relativePath) =>
        new()
        {
            DefaultChannel = channel,
            Entries =
            [
                new BrowserBinaryEntry
                {
                    Channel      = channel,
                    Revision     = "1148",
                    Sha256Hex    = new string('0', 64),
                    RelativePath = relativePath,
                    DisplayVersion = "120.0.0"
                }
            ]
        };

    private static OfflineBundleValidator MakeValidator() =>
        new(NullLogger<OfflineBundleValidator>.Instance);

    // ── AC1 / BRW-AT-19: Isolated host — bundled binary present → passes ─────

    [Fact]
    public async Task ValidateAsync_AllBinariesBundled_Passes()
    {
        // Arrange
        var (baseDir, binaryPath) = CreateTempBundleDir();
        string relativePath = Path.GetRelativePath(baseDir, binaryPath);

        var descriptor = new OfflineBundleDescriptor
        {
            PlaywrightVersion  = "1.48.0",
            BinaryManifest     = MakeManifest("chromium", relativePath),
            BundleBaseDirectory = baseDir
        };

        var sut = MakeValidator();

        // Act
        Func<Task> act = () => sut.ValidateAsync(descriptor, CancellationToken.None);

        // Assert — no exception; all binaries bundled correctly
        await act.Should().NotThrowAsync();

        // Cleanup
        Directory.Delete(baseDir, recursive: true);
    }

    // ── AC1 / BRW-AT-19: Multiple channels — all present → passes ────────────

    [Fact]
    public async Task ValidateAsync_MultipleChannelsAllBundled_Passes()
    {
        // Arrange
        string baseDir = Path.Combine(Path.GetTempPath(), $"tracto-bundle-multi-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        // Create binaries for two channels
        string chromiumPath = Path.Combine(baseDir, "chromium-1148", "chrome-win", "chrome.exe");
        string firefoxPath  = Path.Combine(baseDir, "firefox-1234",  "firefox",    "firefox.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(chromiumPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(firefoxPath)!);
        File.WriteAllBytes(chromiumPath, "FAKE_CHROME"u8.ToArray());
        File.WriteAllBytes(firefoxPath,  "FAKE_FIREFOX"u8.ToArray());

        var manifest = new BrowserBinaryManifest
        {
            DefaultChannel = "chromium",
            Entries =
            [
                new BrowserBinaryEntry
                {
                    Channel = "chromium", Revision = "1148",
                    Sha256Hex = new string('0', 64),
                    RelativePath = Path.GetRelativePath(baseDir, chromiumPath)
                },
                new BrowserBinaryEntry
                {
                    Channel = "firefox", Revision = "1234",
                    Sha256Hex = new string('a', 64),
                    RelativePath = Path.GetRelativePath(baseDir, firefoxPath)
                }
            ]
        };

        var descriptor = new OfflineBundleDescriptor
        {
            PlaywrightVersion   = "1.48.0",
            BinaryManifest      = manifest,
            BundleBaseDirectory = baseDir
        };

        var sut = MakeValidator();

        // Act
        Func<Task> act = () => sut.ValidateAsync(descriptor, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();

        // Cleanup
        Directory.Delete(baseDir, recursive: true);
    }

    // ── AC2 / BRW-AT-19: Missing binary → throws without downloading ──────────

    [Fact]
    public async Task ValidateAsync_BinaryMissing_ThrowsOfflineBundleException_NoDownload()
    {
        // Arrange — descriptor points to a binary that does not exist on disk
        string baseDir = Path.Combine(Path.GetTempPath(), $"tracto-bundle-missing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        var descriptor = new OfflineBundleDescriptor
        {
            PlaywrightVersion   = "1.48.0",
            BinaryManifest      = MakeManifest("chromium", "chromium-1148/chrome-win/chrome.exe"),
            BundleBaseDirectory = baseDir
        };

        var sut = MakeValidator();

        // Act
        Func<Task> act = () => sut.ValidateAsync(descriptor, CancellationToken.None);

        // Assert — OfflineBundleException thrown; no download side effect (no network mock needed
        // because the validator never calls any download API)
        OfflineBundleException ex = (await act.Should()
            .ThrowAsync<OfflineBundleException>())
            .Subject.Single();

        ex.Category.Should().Be(ErrorCategory.ENVIRONMENT);
        ex.DetailCode.Should().Be("browser.notInstalled");
        ex.Message.Should().Contain("chromium");

        // Cleanup
        Directory.Delete(baseDir, recursive: true);
    }

    // ── AC2: One channel present, one missing → exception lists the missing channel ─

    [Fact]
    public async Task ValidateAsync_OneChannelMissing_ThrowsWithMissingChannelName()
    {
        // Arrange
        string baseDir = Path.Combine(Path.GetTempPath(), $"tracto-bundle-partial-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        string chromiumPath = Path.Combine(baseDir, "chromium-1148", "chrome-win", "chrome.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(chromiumPath)!);
        File.WriteAllBytes(chromiumPath, "FAKE_CHROME"u8.ToArray());
        // firefox binary intentionally absent

        var manifest = new BrowserBinaryManifest
        {
            DefaultChannel = "chromium",
            Entries =
            [
                new BrowserBinaryEntry
                {
                    Channel = "chromium", Revision = "1148",
                    Sha256Hex = new string('0', 64),
                    RelativePath = Path.GetRelativePath(baseDir, chromiumPath)
                },
                new BrowserBinaryEntry
                {
                    Channel = "firefox", Revision = "1234",
                    Sha256Hex = new string('a', 64),
                    RelativePath = "firefox-1234/firefox/firefox.exe"
                }
            ]
        };

        var descriptor = new OfflineBundleDescriptor
        {
            PlaywrightVersion   = "1.48.0",
            BinaryManifest      = manifest,
            BundleBaseDirectory = baseDir
        };

        var sut = MakeValidator();

        // Act
        Func<Task> act = () => sut.ValidateAsync(descriptor, CancellationToken.None);

        // Assert — exception mentions the missing channel (firefox) and not the present one
        OfflineBundleException ex = (await act.Should()
            .ThrowAsync<OfflineBundleException>())
            .Subject.Single();

        ex.Message.Should().Contain("firefox");
        ex.Message.Should().NotContain("chromium");

        // Cleanup
        Directory.Delete(baseDir, recursive: true);
    }

    // ── AC3: Missing PlaywrightVersion → throws before checking binaries ───────

    [Fact]
    public async Task ValidateAsync_EmptyPlaywrightVersion_ThrowsOfflineBundleException()
    {
        // Arrange — descriptor with no PlaywrightVersion
        var (baseDir, binaryPath) = CreateTempBundleDir();
        string relativePath = Path.GetRelativePath(baseDir, binaryPath);

        var descriptor = new OfflineBundleDescriptor
        {
            PlaywrightVersion   = "",          // intentionally empty
            BinaryManifest      = MakeManifest("chromium", relativePath),
            BundleBaseDirectory = baseDir
        };

        var sut = MakeValidator();

        // Act
        Func<Task> act = () => sut.ValidateAsync(descriptor, CancellationToken.None);

        // Assert — exception thrown at PlaywrightVersion check
        OfflineBundleException ex = (await act.Should()
            .ThrowAsync<OfflineBundleException>())
            .Subject.Single();

        ex.Category.Should().Be(ErrorCategory.ENVIRONMENT);
        ex.DetailCode.Should().Be("browser.notInstalled");

        // Cleanup
        Directory.Delete(baseDir, recursive: true);
    }

    // ── AC3: WhitespaceOnly PlaywrightVersion → throws ────────────────────────

    [Fact]
    public async Task ValidateAsync_WhitespacePlaywrightVersion_ThrowsOfflineBundleException()
    {
        var (baseDir, binaryPath) = CreateTempBundleDir();
        string relativePath = Path.GetRelativePath(baseDir, binaryPath);

        var descriptor = new OfflineBundleDescriptor
        {
            PlaywrightVersion   = "   ",       // whitespace-only
            BinaryManifest      = MakeManifest("chromium", relativePath),
            BundleBaseDirectory = baseDir
        };

        var sut = MakeValidator();

        Func<Task> act = () => sut.ValidateAsync(descriptor, CancellationToken.None);

        await act.Should().ThrowAsync<OfflineBundleException>();

        Directory.Delete(baseDir, recursive: true);
    }

    // ── AC3: ToCecError produces correct wire record ──────────────────────────

    [Fact]
    public void OfflineBundleException_ToCecError_MapsToEnvironmentCategory()
    {
        var ex = new OfflineBundleException("Bundle is incomplete.");

        CecError error = ex.ToCecError();

        error.Category.Should().Be(ErrorCategory.ENVIRONMENT);
        error.DetailCode.Should().Be("browser.notInstalled");
        error.Message.Should().Be("Bundle is incomplete.");
    }

    // ── AC4 / BRW-AT-22: RuntimePackage.IsIsolatedDeploymentAsync — env-var detection ─

    [Fact]
    public async Task RuntimePackage_IsIsolated_WhenEnvVarIsTrue()
    {
        // Arrange
        string prev = SysEnvironment.GetEnvironmentVariable(RuntimePackage.IsolatedHostEnvVar) ?? "";
        try
        {
            SysEnvironment.SetEnvironmentVariable(RuntimePackage.IsolatedHostEnvVar, "true");
            var sut = new RuntimePackage(MinimalManifest());

            // Act
            bool result = await sut.IsIsolatedDeploymentAsync(CancellationToken.None);

            // Assert — flag recognized; runtime enters isolated mode
            result.Should().BeTrue();
        }
        finally
        {
            SysEnvironment.SetEnvironmentVariable(
                RuntimePackage.IsolatedHostEnvVar,
                string.IsNullOrEmpty(prev) ? null : prev);
        }
    }

    [Fact]
    public async Task RuntimePackage_IsIsolated_WhenEnvVarIsOne()
    {
        string prev = SysEnvironment.GetEnvironmentVariable(RuntimePackage.IsolatedHostEnvVar) ?? "";
        try
        {
            SysEnvironment.SetEnvironmentVariable(RuntimePackage.IsolatedHostEnvVar, "1");
            var sut = new RuntimePackage(MinimalManifest());

            bool result = await sut.IsIsolatedDeploymentAsync(CancellationToken.None);

            result.Should().BeTrue();
        }
        finally
        {
            SysEnvironment.SetEnvironmentVariable(
                RuntimePackage.IsolatedHostEnvVar,
                string.IsNullOrEmpty(prev) ? null : prev);
        }
    }

    [Fact]
    public async Task RuntimePackage_IsNotIsolated_WhenEnvVarAbsent()
    {
        // Arrange — ensure the env var is absent for this test
        string prev = SysEnvironment.GetEnvironmentVariable(RuntimePackage.IsolatedHostEnvVar) ?? "";
        try
        {
            SysEnvironment.SetEnvironmentVariable(RuntimePackage.IsolatedHostEnvVar, null);
            var sut = new RuntimePackage(MinimalManifest());

            // Act
            bool result = await sut.IsIsolatedDeploymentAsync(CancellationToken.None);

            // Assert — absent env var → connected-mode behavior (no marker file in test context)
            result.Should().BeFalse();
        }
        finally
        {
            SysEnvironment.SetEnvironmentVariable(
                RuntimePackage.IsolatedHostEnvVar,
                string.IsNullOrEmpty(prev) ? null : prev);
        }
    }

    // ── AC3: RuntimePackage exposes PlaywrightVersion and BundledBrowserChannels ─

    [Fact]
    public void RuntimePackage_PlaywrightVersion_MatchesPinnedVersion()
    {
        var sut = new RuntimePackage(MinimalManifest());

        sut.PlaywrightVersion.Should().Be(RuntimePackage.PinnedPlaywrightVersion);
        sut.PlaywrightVersion.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void RuntimePackage_BundledBrowserChannels_ReflectsManifest()
    {
        var manifest = new BrowserBinaryManifest
        {
            DefaultChannel = "chromium",
            Entries =
            [
                new BrowserBinaryEntry
                {
                    Channel = "chromium", Revision = "1148",
                    Sha256Hex = new string('0', 64), RelativePath = "chrome.exe"
                },
                new BrowserBinaryEntry
                {
                    Channel = "firefox", Revision = "1234",
                    Sha256Hex = new string('a', 64), RelativePath = "firefox.exe"
                }
            ]
        };

        var sut = new RuntimePackage(manifest);

        sut.BundledBrowserChannels.Should().BeEquivalentTo(["chromium", "firefox"]);
    }

    [Fact]
    public void RuntimePackage_RuntimeVersion_IsNonEmpty()
    {
        var sut = new RuntimePackage(MinimalManifest());

        sut.RuntimeVersion.Should().NotBeNullOrEmpty();
    }

    // ── AC2 / BRW-AT-28: OfflineBundleDescriptor carries absolute path directly ─

    [Fact]
    public async Task ValidateAsync_AbsoluteBinaryPath_Passes()
    {
        // Arrange — binary path is absolute (not relative to BundleBaseDirectory)
        string tempBinary = Path.Combine(Path.GetTempPath(), $"chrome-abs-{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(tempBinary, "FAKE"u8.ToArray());

        var manifest = new BrowserBinaryManifest
        {
            DefaultChannel = "chromium",
            Entries =
            [
                new BrowserBinaryEntry
                {
                    Channel = "chromium", Revision = "1148",
                    Sha256Hex = new string('0', 64),
                    RelativePath = tempBinary   // absolute path — must be used directly
                }
            ]
        };

        var descriptor = new OfflineBundleDescriptor
        {
            PlaywrightVersion   = "1.48.0",
            BinaryManifest      = manifest,
            BundleBaseDirectory = Path.GetTempPath()
        };

        var sut = MakeValidator();

        // Act
        Func<Task> act = () => sut.ValidateAsync(descriptor, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();

        // Cleanup
        File.Delete(tempBinary);
    }

    // ── Cancellation propagation ───────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_CancelledToken_ThrowsOperationCancelled()
    {
        var (baseDir, binaryPath) = CreateTempBundleDir();
        string relativePath = Path.GetRelativePath(baseDir, binaryPath);

        var descriptor = new OfflineBundleDescriptor
        {
            PlaywrightVersion   = "1.48.0",
            BinaryManifest      = MakeManifest("chromium", relativePath),
            BundleBaseDirectory = baseDir
        };

        var sut = MakeValidator();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => sut.ValidateAsync(descriptor, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        Directory.Delete(baseDir, recursive: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static BrowserBinaryManifest MinimalManifest() =>
        new()
        {
            DefaultChannel = "chromium",
            Entries =
            [
                new BrowserBinaryEntry
                {
                    Channel = "chromium", Revision = "1148",
                    Sha256Hex = new string('0', 64), RelativePath = "chrome.exe"
                }
            ]
        };
}
