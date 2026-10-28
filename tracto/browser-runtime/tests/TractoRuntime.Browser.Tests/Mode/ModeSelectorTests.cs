using FluentAssertions;
using Moq;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Mode;
using TractoRuntime.Browser.Tests.Fixtures;

namespace TractoRuntime.Browser.Tests.Mode;

/// <summary>
/// Unit tests for <see cref="ModeSelector"/>.
/// Covers BRW-US-002 AC#1 (headless requires no session) and AC#2 (headed requires session).
/// Acceptance tests: BRW-AT-10 (headless smoke), BRW-AT-22 (headed exclusion / session check).
/// </summary>
public sealed class ModeSelectorTests
{
    private readonly Mock<IWindowsSessionInspector> _sessionInspectorMock = new();
    private readonly ModeSelector _sut;

    public ModeSelectorTests()
    {
        _sut = new ModeSelector(_sessionInspectorMock.Object);
    }

    // ── BRW-AT-10: Headless smoke test ──────────────────────────────────────

    [Fact]
    public void SelectMode_HeadlessScript_ReturnsHeadlessConfig()
    {
        // Arrange
        var script = TestArtifactFactory.MakeScript(BrowserMode.Headless);

        // Act
        var config = _sut.SelectMode(script);

        // Assert — AC#1: mode is headless, no session needed
        config.Mode.Should().Be(BrowserMode.Headless);
        config.InteractiveSessionId.Should().BeNull();
        config.RequiresActiveSession.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateModeAsync_HeadlessConfig_SucceedsWithoutSessionInspection()
    {
        // Arrange — session inspector must NOT be called for headless (AC#1)
        var config = new ExecutionModeConfig { Mode = BrowserMode.Headless };

        // Act
        var result = await _sut.ValidateModeAsync(config, CancellationToken.None);

        // Assert
        result.Mode.Should().Be(BrowserMode.Headless);
        result.InteractiveSessionId.Should().BeNull();

        // Verify session inspector was never queried — headless requires no desktop logon
        _sessionInspectorMock.Verify(
            s => s.GetActiveInteractiveSessionIdAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ValidateModeAsync_HeadlessConfig_DoesNotRequireActiveSession()
    {
        // Arrange — no interactive session available on the host
        _sessionInspectorMock
            .Setup(s => s.GetActiveInteractiveSessionIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var config = new ExecutionModeConfig { Mode = BrowserMode.Headless };

        // Act — must not throw even though there is no Windows session
        var act = async () => await _sut.ValidateModeAsync(config, CancellationToken.None);

        // Assert — AC#1: headless completes without a desktop logon
        await act.Should().NotThrowAsync();
    }

    // ── BRW-AT-22: Headed mode session validation ────────────────────────────

    [Fact]
    public void SelectMode_HeadedScript_ReturnsHeadedConfigWithNullSessionId()
    {
        // Arrange
        var script = TestArtifactFactory.MakeScript(BrowserMode.Headed);

        // Act — session ID is NOT resolved at SelectMode time; it is deferred to ValidateModeAsync
        var config = _sut.SelectMode(script);

        // Assert
        config.Mode.Should().Be(BrowserMode.Headed);
        config.RequiresActiveSession.Should().BeTrue();
        config.InteractiveSessionId.Should().BeNull(); // resolved in ValidateModeAsync
    }

    [Fact]
    public async Task ValidateModeAsync_HeadedConfig_WithActiveSession_ReturnsEnrichedConfig()
    {
        // Arrange — Windows interactive session "1" is available
        const string sessionId = "1";
        _sessionInspectorMock
            .Setup(s => s.GetActiveInteractiveSessionIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionId);

        var config = new ExecutionModeConfig { Mode = BrowserMode.Headed };

        // Act
        var result = await _sut.ValidateModeAsync(config, CancellationToken.None);

        // Assert — AC#2, AC#4: session ID is resolved and returned for advertisement
        result.Mode.Should().Be(BrowserMode.Headed);
        result.InteractiveSessionId.Should().Be(sessionId);
    }

    [Fact]
    public async Task ValidateModeAsync_HeadedConfig_WithNoSession_ThrowsModeValidationException()
    {
        // Arrange — no interactive session on the host (e.g. headless server or service account)
        _sessionInspectorMock
            .Setup(s => s.GetActiveInteractiveSessionIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var config = new ExecutionModeConfig { Mode = BrowserMode.Headed };

        // Act
        var act = async () => await _sut.ValidateModeAsync(config, CancellationToken.None);

        // Assert — AC#2: no session → SESSION_UNAVAILABLE
        await act.Should().ThrowAsync<ModeValidationException>()
            .Where(ex => ex.Mode == BrowserMode.Headed &&
                         ex.ErrorCategory == ErrorCategory.SESSION_UNAVAILABLE);
    }

    [Fact]
    public async Task ValidateModeAsync_HeadedConfig_CancellationRespected()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var config = new ExecutionModeConfig { Mode = BrowserMode.Headed };

        _sessionInspectorMock
            .Setup(s => s.GetActiveInteractiveSessionIdAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        var act = async () => await _sut.ValidateModeAsync(config, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
