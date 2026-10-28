using FluentAssertions;
using TractoRuntime.Browser.Mode;

namespace TractoRuntime.Browser.Tests.Mode;

/// <summary>
/// Unit tests for <see cref="ForegroundInputOwner"/>.
/// Covers BRW-US-002 AC#2 (exclusive foreground-input ownership) and
/// AC#3 (second attempt for same session is rejected).
/// Acceptance tests: BRW-AT-22 (headed exclusion).
/// </summary>
public sealed class ForegroundInputOwnerTests : IAsyncDisposable
{
    private readonly ForegroundInputOwner _sut = new();
    private const string SessionA = "session-test-A-" + nameof(ForegroundInputOwnerTests);
    private const string SessionB = "session-test-B-" + nameof(ForegroundInputOwnerTests);

    // Clean up any ownership state left by this test class (static dict is process-wide).
    public async ValueTask DisposeAsync()
    {
        await _sut.ReleaseAsync("run-001", SessionA, CancellationToken.None);
        await _sut.ReleaseAsync("run-002", SessionA, CancellationToken.None);
        await _sut.ReleaseAsync("run-001", SessionB, CancellationToken.None);
        await _sut.DisposeAsync();
    }

    // ── AC#2: Exclusive ownership acquisition ───────────────────────────────

    [Fact]
    public async Task TryAcquireAsync_FirstCaller_AcquiresOwnership()
    {
        // Arrange
        const string runId = "run-001";

        // Act
        var result = await _sut.TryAcquireAsync(runId, SessionA, CancellationToken.None);

        // Assert — AC#2: first caller succeeds
        result.Acquired.Should().BeTrue();
        result.ConflictingRunId.Should().BeNull();
        result.SessionId.Should().Be(SessionA);

        // Verify owner is recorded
        _sut.GetCurrentOwner(SessionA).Should().Be(runId);

        // Cleanup
        await _sut.ReleaseAsync(runId, SessionA, CancellationToken.None);
    }

    // ── AC#3: Second attempt for same session is rejected ───────────────────

    [Fact]
    public async Task TryAcquireAsync_SecondCallerSameSession_IsRejected()
    {
        // Arrange — first headed run acquires ownership
        const string firstRunId = "run-001";
        const string secondRunId = "run-002";

        await _sut.TryAcquireAsync(firstRunId, SessionA, CancellationToken.None);

        // Act — second headed run (or Desktop worker) attempts the same session
        var result = await _sut.TryAcquireAsync(secondRunId, SessionA, CancellationToken.None);

        // Assert — AC#3: rejected; conflicting owner is reported for diagnostics
        result.Acquired.Should().BeFalse();
        result.ConflictingRunId.Should().Be(firstRunId);
        result.SessionId.Should().Be(SessionA);

        // Cleanup
        await _sut.ReleaseAsync(firstRunId, SessionA, CancellationToken.None);
    }

    [Fact]
    public async Task TryAcquireAsync_DifferentSessions_DoNotConflict()
    {
        // Arrange — two separate Windows sessions
        const string runA = "run-001";
        const string runB = "run-browser-B";

        // Act — one headed worker per session; must not conflict
        var resultA = await _sut.TryAcquireAsync(runA, SessionA, CancellationToken.None);
        var resultB = await _sut.TryAcquireAsync(runB, SessionB, CancellationToken.None);

        // Assert — independent sessions are independent
        resultA.Acquired.Should().BeTrue();
        resultB.Acquired.Should().BeTrue();

        // Cleanup
        await _sut.ReleaseAsync(runA, SessionA, CancellationToken.None);
        await _sut.ReleaseAsync(runB, SessionB, CancellationToken.None);
    }

    [Fact]
    public async Task ReleaseAsync_AfterRelease_NewCallerCanAcquire()
    {
        // Arrange — first run acquires then releases
        const string firstRun = "run-001";
        const string secondRun = "run-002";

        await _sut.TryAcquireAsync(firstRun, SessionA, CancellationToken.None);
        await _sut.ReleaseAsync(firstRun, SessionA, CancellationToken.None);

        // Act — second run should now be able to acquire
        var result = await _sut.TryAcquireAsync(secondRun, SessionA, CancellationToken.None);

        // Assert — session is free after release
        result.Acquired.Should().BeTrue();
        _sut.GetCurrentOwner(SessionA).Should().Be(secondRun);

        // Cleanup
        await _sut.ReleaseAsync(secondRun, SessionA, CancellationToken.None);
    }

    [Fact]
    public async Task ReleaseAsync_NonOwner_DoesNotReleaseOwnership()
    {
        // Arrange — run-001 owns the session
        const string owner = "run-001";
        const string nonOwner = "run-999";

        await _sut.TryAcquireAsync(owner, SessionA, CancellationToken.None);

        // Act — a non-owner tries to release (should be a no-op)
        await _sut.ReleaseAsync(nonOwner, SessionA, CancellationToken.None);

        // Assert — original owner still holds the session
        _sut.GetCurrentOwner(SessionA).Should().Be(owner);

        // Cleanup
        await _sut.ReleaseAsync(owner, SessionA, CancellationToken.None);
    }

    [Fact]
    public async Task GetCurrentOwner_NoOwner_ReturnsNull()
    {
        // Arrange — ensure no owner for a fresh session key
        const string unownedSession = "session-unowned-" + nameof(GetCurrentOwner_NoOwner_ReturnsNull);

        // Act
        string? owner = _sut.GetCurrentOwner(unownedSession);

        // Assert
        owner.Should().BeNull();

        await Task.CompletedTask;
    }
}
