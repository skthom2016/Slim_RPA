using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Moq;
using TractoRuntime.Browser.Context;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Tests.Fixtures;

namespace TractoRuntime.Browser.Tests.Context;

/// <summary>
/// Unit tests for <see cref="BrowserContextManager"/> covering BRW-US-003 acceptance criteria.
/// Acceptance tests: BRW-AT-13 (isolation) and BRW-AT-27 (cleanup on run completion).
/// </summary>
public sealed class BrowserContextManagerTests : BrowserTestBase
{
    private readonly Mock<IBrowserFactory> _factoryMock = new();
    private readonly Mock<IBrowser> _browserMock = new();

    public BrowserContextManagerTests()
    {
        // Default: factory always returns the shared browser mock
        _factoryMock
            .Setup(f => f.LaunchAsync(It.IsAny<BrowserMode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_browserMock.Object);

        // Default browser close is a no-op
        _browserMock
            .Setup(b => b.CloseAsync(It.IsAny<BrowserCloseOptions?>()))
            .Returns(Task.CompletedTask);
    }

    // -------------------------------------------------------------------------
    // BRW-AT-13 — Per-run context isolation
    // -------------------------------------------------------------------------

    [Fact(DisplayName = "BRW-AT-13 (AC#1): Each run acquires a distinct, non-shared IBrowserContext")]
    public async Task AcquireContextAsync_TwoRuns_ProducesDistinctContextObjects()
    {
        // Arrange
        var ctxA = new Mock<IBrowserContext>().Object;
        var ctxB = new Mock<IBrowserContext>().Object;

        _browserMock
            .SetupSequence(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .ReturnsAsync(ctxA)
            .ReturnsAsync(ctxB);

        await using var sut = CreateSut();

        // Act
        var resultA = await sut.AcquireContextAsync("run-A", BrowserMode.Headless, default);
        var resultB = await sut.AcquireContextAsync("run-B", BrowserMode.Headless, default);

        // Assert — two different context objects; AC#2: concurrent runs cannot share state
        resultA.Should().NotBeSameAs(resultB,
            "each run must receive its own isolated browser context");
    }

    [Fact(DisplayName = "BRW-AT-13 (AC#1): Default context has null StorageState (no inherited cookies)")]
    public async Task AcquireContextAsync_NoApprovedProfile_ContextCreatedWithNullStorageState()
    {
        // Arrange
        BrowserNewContextOptions? captured = null;

        _browserMock
            .Setup(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .Callback<BrowserNewContextOptions?>(o => captured = o)
            .ReturnsAsync(new Mock<IBrowserContext>().Object);

        await using var sut = CreateSut();

        // Act — no RegisterApprovedProfile → fresh context
        await sut.AcquireContextAsync("run-fresh", BrowserMode.Headless, default);

        // Assert
        captured.Should().NotBeNull();
        captured!.StorageState.Should().BeNull(
            "a fresh run must not inherit cookies or local-storage from any previous run");
    }

    [Fact(DisplayName = "BRW-AT-13 (AC#2): Duplicate AcquireContextAsync for the same runId throws")]
    public async Task AcquireContextAsync_SameRunIdTwice_ThrowsInvalidOperationException()
    {
        // Arrange
        _browserMock
            .Setup(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .ReturnsAsync(new Mock<IBrowserContext>().Object);

        await using var sut = CreateSut();
        await sut.AcquireContextAsync("run-dup", BrowserMode.Headless, default);

        // Act & Assert — second call must be rejected (not silently share the same context)
        await sut.Invoking(s => s.AcquireContextAsync("run-dup", BrowserMode.Headless, default))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*run-dup*");
    }

    [Fact(DisplayName = "BRW-AT-13 (AC#3): Approved profile loads storageState from vault only")]
    public async Task AcquireContextAsync_WithApprovedProfile_LoadsStorageStateFromVault()
    {
        // Arrange
        const string runId = "run-approved";
        const string signedRef = "vault://sessions/signed-ref-001";
        const string storageStateJson = "{\"cookies\":[],\"origins\":[]}";

        BrowserNewContextOptions? captured = null;

        _browserMock
            .Setup(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .Callback<BrowserNewContextOptions?>(o => captured = o)
            .ReturnsAsync(new Mock<IBrowserContext>().Object);

        SessionStoreMock
            .Setup(s => s.LoadStorageStateAsync(signedRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(storageStateJson);

        await using var sut = CreateSut();

        // Explicit signed policy reference — as the worker sets after validating the artifact
        sut.RegisterApprovedProfile(runId, signedRef);

        // Act
        await sut.AcquireContextAsync(runId, BrowserMode.Headless, default);

        // Assert — vault sourced, never from raw script input
        captured!.StorageState.Should().Be(storageStateJson,
            "approved storageState must be loaded from the vault via ISessionStore");

        SessionStoreMock.Verify(
            s => s.LoadStorageStateAsync(signedRef, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact(DisplayName = "BRW-AT-13 (AC#3): Without registered profile the context is always fresh")]
    public async Task AcquireContextAsync_NoRegisteredProfile_AlwaysFreshContext()
    {
        // Arrange — StorageState should not be loaded at all
        _browserMock
            .Setup(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .ReturnsAsync(new Mock<IBrowserContext>().Object);

        await using var sut = CreateSut();

        // Act — deliberately do NOT call RegisterApprovedProfile
        await sut.AcquireContextAsync("run-no-profile", BrowserMode.Headless, default);

        // Assert — ISessionStore must not have been consulted
        SessionStoreMock.Verify(
            s => s.LoadStorageStateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "ad-hoc profile sharing without a signed policy reference must not occur");
    }

    // -------------------------------------------------------------------------
    // BRW-AT-27 — Context cleanup on run completion
    // -------------------------------------------------------------------------

    [Fact(DisplayName = "BRW-AT-27 (AC#4): ReleaseContextAsync closes the context after successful run")]
    public async Task ReleaseContextAsync_AfterSuccess_ContextIsClosed()
    {
        // Arrange
        var ctxMock = new Mock<IBrowserContext>();
        ctxMock
            .Setup(c => c.CloseAsync(It.IsAny<BrowserContextCloseOptions?>()))
            .Returns(Task.CompletedTask);

        _browserMock
            .Setup(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .ReturnsAsync(ctxMock.Object);

        await using var sut = CreateSut();
        await sut.AcquireContextAsync("run-ok", BrowserMode.Headless, default);

        // Act
        await sut.ReleaseContextAsync("run-ok", default);

        // Assert
        ctxMock.Verify(
            c => c.CloseAsync(It.IsAny<BrowserContextCloseOptions?>()),
            Times.Once,
            "browser context must be fully closed after the run completes");
    }

    [Fact(DisplayName = "BRW-AT-27 (AC#4): ReleaseContextAsync closes context after failure")]
    public async Task ReleaseContextAsync_AfterFailure_ContextIsStillClosed()
    {
        // Arrange — same expectation regardless of run outcome
        var ctxMock = new Mock<IBrowserContext>();
        ctxMock
            .Setup(c => c.CloseAsync(It.IsAny<BrowserContextCloseOptions?>()))
            .Returns(Task.CompletedTask);

        _browserMock
            .Setup(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .ReturnsAsync(ctxMock.Object);

        await using var sut = CreateSut();
        await sut.AcquireContextAsync("run-fail", BrowserMode.Headless, default);

        // Act — simulate the worker calling release on a failed run
        await sut.ReleaseContextAsync("run-fail", default);

        ctxMock.Verify(
            c => c.CloseAsync(It.IsAny<BrowserContextCloseOptions?>()),
            Times.Once);
    }

    [Fact(DisplayName = "BRW-AT-27 (AC#4): ReleaseContextAsync clears the approved profile reference")]
    public async Task ReleaseContextAsync_ClearsApprovedProfileRef_FreshContextOnNextAcquire()
    {
        // Arrange
        const string runId = "run-profile-lifecycle";
        const string signedRef = "vault://sessions/ref-lifecycle";
        const string storageStateJson = "{\"cookies\":[]}";

        var ctxMock1 = new Mock<IBrowserContext>();
        ctxMock1.Setup(c => c.CloseAsync(It.IsAny<BrowserContextCloseOptions?>())).Returns(Task.CompletedTask);

        var ctxMock2 = new Mock<IBrowserContext>();
        ctxMock2.Setup(c => c.CloseAsync(It.IsAny<BrowserContextCloseOptions?>())).Returns(Task.CompletedTask);

        _browserMock
            .SetupSequence(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .ReturnsAsync(ctxMock1.Object)
            .ReturnsAsync(ctxMock2.Object);

        SessionStoreMock
            .Setup(s => s.LoadStorageStateAsync(signedRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(storageStateJson);

        await using var sut = CreateSut();

        // First run — approved profile registered
        sut.RegisterApprovedProfile(runId, signedRef);
        await sut.AcquireContextAsync(runId, BrowserMode.Headless, default);
        await sut.ReleaseContextAsync(runId, default);

        // Second run — profile reference must have been cleared; fresh context expected
        BrowserNewContextOptions? capturedSecond = null;
        _browserMock
            .Setup(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .Callback<BrowserNewContextOptions?>(o => capturedSecond = o)
            .ReturnsAsync(ctxMock2.Object);

        await sut.AcquireContextAsync(runId, BrowserMode.Headless, default);

        // Assert — second acquire must have null StorageState (profile ref was cleared after release)
        capturedSecond!.StorageState.Should().BeNull(
            "the approved profile reference must be cleared after ReleaseContextAsync so a " +
            "subsequent run starts completely fresh");

        // ISessionStore consulted exactly once (for the first run only)
        SessionStoreMock.Verify(
            s => s.LoadStorageStateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact(DisplayName = "BRW-AT-27 (AC#4): DisposeAsync closes all unreleased run contexts")]
    public async Task DisposeAsync_ClosesAllUnreleasedContexts()
    {
        // Arrange
        var ctxMock1 = new Mock<IBrowserContext>();
        var ctxMock2 = new Mock<IBrowserContext>();
        ctxMock1.Setup(c => c.CloseAsync(It.IsAny<BrowserContextCloseOptions?>())).Returns(Task.CompletedTask);
        ctxMock2.Setup(c => c.CloseAsync(It.IsAny<BrowserContextCloseOptions?>())).Returns(Task.CompletedTask);

        _browserMock
            .SetupSequence(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .ReturnsAsync(ctxMock1.Object)
            .ReturnsAsync(ctxMock2.Object);

        var sut = CreateSut(); // not using 'await using' so we control dispose timing

        await sut.AcquireContextAsync("run-unreleased-1", BrowserMode.Headless, default);
        await sut.AcquireContextAsync("run-unreleased-2", BrowserMode.Headless, default);

        // Act — dispose without explicit ReleaseContextAsync
        await sut.DisposeAsync();

        // Assert — all contexts closed even without explicit release calls
        ctxMock1.Verify(c => c.CloseAsync(It.IsAny<BrowserContextCloseOptions?>()), Times.Once,
            "context for run-unreleased-1 must be closed on disposal");
        ctxMock2.Verify(c => c.CloseAsync(It.IsAny<BrowserContextCloseOptions?>()), Times.Once,
            "context for run-unreleased-2 must be closed on disposal");

        // Browser process also closed
        _browserMock.Verify(b => b.CloseAsync(It.IsAny<BrowserCloseOptions?>()), Times.Once,
            "browser process must be closed during disposal to prevent orphaned processes");
    }

    [Fact(DisplayName = "BRW-AT-27: ReleaseContextAsync on unknown runId is a no-op")]
    public async Task ReleaseContextAsync_UnknownRunId_IsNoOp()
    {
        await using var sut = CreateSut();

        // Act — should not throw
        await sut.Invoking(s => s.ReleaseContextAsync("run-never-acquired", default))
            .Should().NotThrowAsync();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private BrowserContextManager CreateSut() =>
        new(
            _factoryMock.Object,
            new BrowserContextManagerOptions(),
            SessionStoreMock.Object,
            NullLogger<BrowserContextManager>.Instance);
}
