using FluentAssertions;
using Moq;
using TractoRuntime.Browser.Capabilities;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Mode;

namespace TractoRuntime.Browser.Tests.Capabilities;

/// <summary>
/// Unit tests for <see cref="CapabilityAdvertiser"/>.
/// Covers BRW-US-002 AC#4 (mode and interactiveSessionId in capability advertisement)
/// and BRW-006 (engine/version metadata).
/// Acceptance tests: BRW-AT-10 (headless payload), BRW-AT-22 (headed payload with session).
/// </summary>
public sealed class CapabilityAdvertiserTests
{
    private readonly Mock<IRuntimeInfo> _runtimeInfoMock = new();
    private readonly CapabilityAdvertiser _sut;

    public CapabilityAdvertiserTests()
    {
        _runtimeInfoMock
            .Setup(r => r.GetBrowserInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(("chromium", "1.48.0"));

        _sut = new CapabilityAdvertiser(_runtimeInfoMock.Object);
    }

    // ── BRW-AT-10: Headless capability payload ───────────────────────────────

    [Fact]
    public async Task BuildPayloadAsync_HeadlessMode_PayloadHasCorrectMode()
    {
        // Arrange
        var config = new ExecutionModeConfig { Mode = BrowserMode.Headless };

        // Act
        var payload = await _sut.BuildPayloadAsync("worker-001", config, CancellationToken.None);

        // Assert — AC#4: mode is "headless"
        payload.Mode.Should().Be("headless");
        payload.RuntimeType.Should().Be("browser");
        payload.WorkerId.Should().Be("worker-001");
    }

    [Fact]
    public async Task BuildPayloadAsync_HeadlessMode_InteractiveSessionIdIsNull()
    {
        // Arrange
        var config = new ExecutionModeConfig { Mode = BrowserMode.Headless };

        // Act
        var payload = await _sut.BuildPayloadAsync("worker-001", config, CancellationToken.None);

        // Assert — AC#4: no interactiveSessionId for headless
        payload.InteractiveSessionId.Should().BeNull();
    }

    [Fact]
    public async Task BuildPayloadAsync_HeadlessMode_CapabilitiesContainHeadlessToken()
    {
        // Arrange
        var config = new ExecutionModeConfig { Mode = BrowserMode.Headless };

        // Act
        var payload = await _sut.BuildPayloadAsync("worker-001", config, CancellationToken.None);

        // Assert
        payload.Capabilities.Should().Contain("browser.headless");
        payload.Capabilities.Should().Contain("browser.engine.chromium");
        payload.Capabilities.Should().NotContain(c => c.StartsWith("browser.session."));
    }

    // ── BRW-AT-22: Headed capability payload ────────────────────────────────

    [Fact]
    public async Task BuildPayloadAsync_HeadedMode_PayloadHasCorrectMode()
    {
        // Arrange — headed worker with active session "2"
        var config = new ExecutionModeConfig { Mode = BrowserMode.Headed, InteractiveSessionId = "2" };

        // Act
        var payload = await _sut.BuildPayloadAsync("worker-002", config, CancellationToken.None);

        // Assert — AC#4: mode is "headed"
        payload.Mode.Should().Be("headed");
        payload.RuntimeType.Should().Be("browser");
    }

    [Fact]
    public async Task BuildPayloadAsync_HeadedMode_InteractiveSessionIdIsReported()
    {
        // Arrange
        var config = new ExecutionModeConfig { Mode = BrowserMode.Headed, InteractiveSessionId = "2" };

        // Act
        var payload = await _sut.BuildPayloadAsync("worker-002", config, CancellationToken.None);

        // Assert — AC#4: interactiveSessionId is present for headed mode
        payload.InteractiveSessionId.Should().Be("2");
    }

    [Fact]
    public async Task BuildPayloadAsync_HeadedMode_CapabilitiesContainHeadedAndSessionTokens()
    {
        // Arrange
        var config = new ExecutionModeConfig { Mode = BrowserMode.Headed, InteractiveSessionId = "3" };

        // Act
        var payload = await _sut.BuildPayloadAsync("worker-002", config, CancellationToken.None);

        // Assert
        payload.Capabilities.Should().Contain("browser.headed");
        payload.Capabilities.Should().Contain("browser.engine.chromium");
        payload.Capabilities.Should().Contain("browser.session.3");
    }

    [Fact]
    public async Task BuildPayloadAsync_HeadedModeWithNullSessionId_NoSessionTokenInCapabilities()
    {
        // Arrange — session ID not yet resolved (edge case: advertisement before validation)
        var config = new ExecutionModeConfig { Mode = BrowserMode.Headed, InteractiveSessionId = null };

        // Act
        var payload = await _sut.BuildPayloadAsync("worker-002", config, CancellationToken.None);

        // Assert — no session token when session ID is unknown
        payload.Capabilities.Should().NotContain(c => c.StartsWith("browser.session."));
    }

    // ── Digest integrity ─────────────────────────────────────────────────────

    [Fact]
    public async Task BuildPayloadAsync_CapabilityDigest_IsNonEmptyHexString()
    {
        // Arrange
        var config = new ExecutionModeConfig { Mode = BrowserMode.Headless };

        // Act
        var payload = await _sut.BuildPayloadAsync("worker-001", config, CancellationToken.None);

        // Assert — digest is a non-empty lowercase hex string (integrity hint only)
        payload.CapabilityDigest.Should().NotBeNullOrEmpty();
        payload.CapabilityDigest!.Should().MatchRegex("^[0-9a-f]+$");
    }

    [Fact]
    public async Task BuildPayloadAsync_SameInputTwice_ProducesSameDigest()
    {
        // Arrange — digest is deterministic (integrity hint, not timestamp-based)
        var config = new ExecutionModeConfig { Mode = BrowserMode.Headless };

        // Act
        var p1 = await _sut.BuildPayloadAsync("w1", config, CancellationToken.None);
        var p2 = await _sut.BuildPayloadAsync("w1", config, CancellationToken.None);

        // Assert
        p1.CapabilityDigest.Should().Be(p2.CapabilityDigest);
    }

    // ── Browser metadata (BRW-006) ───────────────────────────────────────────

    [Fact]
    public async Task BuildPayloadAsync_BrowserEngineAndVersion_AreReported()
    {
        // Arrange
        var config = new ExecutionModeConfig { Mode = BrowserMode.Headless };

        // Act
        var payload = await _sut.BuildPayloadAsync("worker-001", config, CancellationToken.None);

        // Assert — BRW-006: engine/version must be in the advertisement
        payload.BrowserEngine.Should().Be("chromium");
        payload.BrowserVersion.Should().Be("1.48.0");
    }
}
