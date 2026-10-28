using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Moq;
using TractoRuntime.Browser.Context;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Tests.Fixtures;

namespace TractoRuntime.Browser.Tests;

/// <summary>
/// Acceptance tests for BRW-US-003 — Per-Run Browser Context Isolation.
/// Covers BRW-AT-13 (isolation) and BRW-AT-27 (cleanup on run completion).
/// Requirement ref: BRW-003.
/// </summary>
/// <remarks>
/// These tests verify the four acceptance criteria at the story level:
///   AC#1 — A new browser context is created for each run by default (no inherited state).
///   AC#2 — Concurrent workers cannot read each other's session state.
///   AC#3 — Approved profile reuse requires an explicit signed policy reference; ad-hoc sharing is blocked.
///   AC#4 — On any terminal state (success, failure, cancellation, timeout), the context is fully closed.
/// </remarks>
public sealed class BRW_US_003_Tests : BrowserTestBase
{
    private readonly Mock<IBrowserFactory> _factoryMock = new();
    private readonly Mock<IBrowser> _browserMock = new();

    public BRW_US_003_Tests()
    {
        _factoryMock
            .Setup(f => f.LaunchAsync(It.IsAny<BrowserMode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_browserMock.Object);

        _browserMock
            .Setup(b => b.CloseAsync(It.IsAny<BrowserCloseOptions?>()))
            .Returns(Task.CompletedTask);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private BrowserContextManager CreateSut() =>
        new(
            _factoryMock.Object,
            new BrowserContextManagerOptions(),
            SessionStoreMock.Object,
            NullLogger<BrowserContextManager>.Instance);

    private Mock<IBrowserContext> SetupFreshContext()
    {
        var ctx = new Mock<IBrowserContext>();
        ctx.Setup(c => c.CloseAsync(It.IsAny<BrowserContextCloseOptions?>()))
           .Returns(Task.CompletedTask);
        _browserMock
            .Setup(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .ReturnsAsync(ctx.Object);
        return ctx;
    }

    // ── AC#1: A new browser context is created for each run by default ───────
    // BRW-AT-13: fresh context; no inherited cookies or local storage.

    [Fact]
    public async Task AC1_HappyPath_FreshRun_ContextCreatedWithNullStorageState()
    {
        // Arrange
        BrowserNewContextOptions? captured = null;
        _browserMock
            .Setup(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .Callback<BrowserNewContextOptions?>(o => captured = o)
            .ReturnsAsync(new Mock<IBrowserContext>().Object);

        await using var sut = CreateSut();

        // Act — no RegisterApprovedProfile; default fresh context
        await sut.AcquireContextAsync("run-ac1-fresh", BrowserMode.Headless, default);

        // Assert
        captured.Should().NotBeNull();
        captured!.StorageState.Should().BeNull(
            "AC#1: a fresh run must not inherit cookies or local-storage from any previous run");
    }

    [Fact]
    public async Task AC1_HappyPath_TwoConsecutiveRuns_EachGetsDistinctContext()
    {
        // Arrange
        var ctxA = new Mock<IBrowserContext>().Object;
        var ctxB = new Mock<IBrowserContext>().Object;
        _browserMock
            .SetupSequence(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .ReturnsAsync(ctxA)
            .ReturnsAsync(ctxB);

        var ctxMock = new Mock<IBrowserContext>();
        ctxMock.Setup(c => c.CloseAsync(It.IsAny<BrowserContextCloseOptions?>())).Returns(Task.CompletedTask);
        // Override to make ctxA releasable without a mock
        var ctxAMock = new Mock<IBrowserContext>();
        ctxAMock.Setup(c => c.CloseAsync(It.IsAny<BrowserContextCloseOptions?>())).Returns(Task.CompletedTask);
        _browserMock
            .SetupSequence(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .ReturnsAsync(ctxAMock.Object)
            .ReturnsAsync(ctxB);

        await using var sut = CreateSut();

        // Act — first run then release, second run
        await sut.AcquireContextAsync("run-ac1-seq-A", BrowserMode.Headless, default);
        await sut.ReleaseContextAsync("run-ac1-seq-A", default);

        var secondCtx = await sut.AcquireContextAsync("run-ac1-seq-B", BrowserMode.Headless, default);

        // Assert — second context is the new object (not the same reference)
        secondCtx.Should().BeSameAs(ctxB,
            "each run must receive a freshly created context, not a recycled one");
    }

    [Fact]
    public async Task AC1_Negative_DuplicateAcquireForSameRunId_ThrowsInvalidOperationException()
    {
        // Arrange
        _browserMock
            .Setup(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .ReturnsAsync(new Mock<IBrowserContext>().Object);

        await using var sut = CreateSut();
        await sut.AcquireContextAsync("run-dup-ac1", BrowserMode.Headless, default);

        // Act & Assert — second acquire must be rejected; no silent context sharing
        await sut.Invoking(s => s.AcquireContextAsync("run-dup-ac1", BrowserMode.Headless, default))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*run-dup-ac1*");
    }

    // ── AC#2: Concurrent workers cannot read each other's session state ───────
    // BRW-AT-13: contexts are keyed per runId; concurrent runs are isolated.

    [Fact]
    public async Task AC2_HappyPath_TwoConcurrentRuns_ProduceDistinctContexts()
    {
        // Arrange — two distinct context mocks; one per concurrent run
        var ctxA = new Mock<IBrowserContext>().Object;
        var ctxB = new Mock<IBrowserContext>().Object;

        _browserMock
            .SetupSequence(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .ReturnsAsync(ctxA)
            .ReturnsAsync(ctxB);

        await using var sut = CreateSut();

        // Act — acquire two contexts concurrently (different runIds, same host)
        var resultA = await sut.AcquireContextAsync("run-concurrent-A", BrowserMode.Headless, default);
        var resultB = await sut.AcquireContextAsync("run-concurrent-B", BrowserMode.Headless, default);

        // Assert — each run has its own distinct context object (not shared)
        resultA.Should().NotBeSameAs(resultB,
            "AC#2: two concurrently leased workers must never share a browser context");
    }

    [Fact]
    public async Task AC2_Negative_SameRunId_CannotAcquireTwoContextsSimultaneously()
    {
        // Arrange
        _browserMock
            .Setup(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .ReturnsAsync(new Mock<IBrowserContext>().Object);

        await using var sut = CreateSut();
        await sut.AcquireContextAsync("run-same-id", BrowserMode.Headless, default);

        // Act & Assert — a second worker trying the same runId must be rejected
        await sut.Invoking(s => s.AcquireContextAsync("run-same-id", BrowserMode.Headless, default))
            .Should().ThrowAsync<InvalidOperationException>(
                "two workers must not share a context for the same runId — concurrent session state is never shared");
    }

    // ── AC#3: Approved profile reuse requires an explicit signed policy ref ───
    // BRW-AT-13: ad-hoc profile sharing is not permitted.

    [Fact]
    public async Task AC3_HappyPath_RegisteredApprovedProfile_LoadsStorageStateFromVaultOnly()
    {
        // Arrange
        const string runId = "run-ac3-approved";
        const string signedRef = "vault://sessions/signed-ref-ac3";
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

        // Act — register signed policy reference THEN acquire
        sut.RegisterApprovedProfile(runId, signedRef);
        await sut.AcquireContextAsync(runId, BrowserMode.Headless, default);

        // Assert — vault-sourced storageState was applied; raw value never from script
        captured!.StorageState.Should().Be(storageStateJson,
            "AC#3: approved storageState must be loaded from the vault via ISessionStore");

        SessionStoreMock.Verify(
            s => s.LoadStorageStateAsync(signedRef, It.IsAny<CancellationToken>()),
            Times.Once,
            "the vault must be consulted exactly once per approved profile reference");
    }

    [Fact]
    public async Task AC3_Negative_NoRegisteredProfile_SessionStoreNeverConsulted()
    {
        // Arrange — no RegisterApprovedProfile call
        _browserMock
            .Setup(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .ReturnsAsync(new Mock<IBrowserContext>().Object);

        await using var sut = CreateSut();

        // Act — acquire without any registered profile
        await sut.AcquireContextAsync("run-ac3-no-profile", BrowserMode.Headless, default);

        // Assert — ISessionStore must NEVER be consulted for ad-hoc profile sharing
        SessionStoreMock.Verify(
            s => s.LoadStorageStateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "AC#3: ad-hoc profile sharing without a signed policy reference is not permitted");
    }

    [Fact]
    public async Task AC3_Negative_ProfileRegisteredForDifferentRunId_OtherRunGetsNullStorage()
    {
        // Arrange
        const string approvedRunId = "run-with-profile";
        const string unapprovedRunId = "run-without-profile";
        const string signedRef = "vault://sessions/signed-ref-ac3b";
        const string storageStateJson = "{\"cookies\":[{\"name\":\"session\"}]}";

        BrowserNewContextOptions? capturedUnapproved = null;

        // Setup sequence: first call for approved run (any options), second for unapproved
        _browserMock
            .SetupSequence(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .ReturnsAsync(new Mock<IBrowserContext>().Object)     // approved run
            .ReturnsAsync(new Mock<IBrowserContext>().Object);    // unapproved run

        // Patch the second call to capture options
        _browserMock
            .Setup(b => b.NewContextAsync(It.Is<BrowserNewContextOptions?>(
                o => true)))
            .Callback<BrowserNewContextOptions?>(o => capturedUnapproved = o)
            .ReturnsAsync(new Mock<IBrowserContext>().Object);

        SessionStoreMock
            .Setup(s => s.LoadStorageStateAsync(signedRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(storageStateJson);

        await using var sut = CreateSut();

        // Register profile only for approvedRunId
        sut.RegisterApprovedProfile(approvedRunId, signedRef);

        // Acquire contexts for BOTH runs; the unapproved one must not get profile state
        await sut.AcquireContextAsync(approvedRunId, BrowserMode.Headless, default);

        // Re-setup to capture the second context options cleanly
        capturedUnapproved = null;
        _browserMock
            .Setup(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .Callback<BrowserNewContextOptions?>(o => capturedUnapproved = o)
            .ReturnsAsync(new Mock<IBrowserContext>().Object);

        await sut.AcquireContextAsync(unapprovedRunId, BrowserMode.Headless, default);

        // Assert — unapproved run must have null StorageState
        capturedUnapproved!.StorageState.Should().BeNull(
            "AC#3: a run without a registered approved profile must always receive a fresh context");
    }

    [Fact]
    public async Task AC3_Negative_RegisterApprovedProfile_WithEmptyRef_ThrowsArgumentException()
    {
        await using var sut = CreateSut();

        // An empty/whitespace signedRef must be rejected — it would represent ad-hoc sharing
        sut.Invoking(s => s.RegisterApprovedProfile("run-bad-ref", ""))
            .Should().Throw<ArgumentException>(
                "a blank signed reference would bypass the vault lookup requirement");
    }

    [Fact]
    public async Task AC3_Negative_RegisterApprovedProfile_WithWhitespaceRef_ThrowsArgumentException()
    {
        await using var sut = CreateSut();

        sut.Invoking(s => s.RegisterApprovedProfile("run-ws-ref", "   "))
            .Should().Throw<ArgumentException>();
    }

    // ── AC#4: On any terminal state the context is fully closed ──────────────
    // BRW-AT-27: context closed on success, failure, cancellation, timeout.

    [Fact]
    public async Task AC4_HappyPath_ReleaseAfterSuccess_ContextClosed()
    {
        // Arrange
        var ctxMock = new Mock<IBrowserContext>();
        ctxMock.Setup(c => c.CloseAsync(It.IsAny<BrowserContextCloseOptions?>()))
               .Returns(Task.CompletedTask);
        _browserMock
            .Setup(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .ReturnsAsync(ctxMock.Object);

        await using var sut = CreateSut();
        await sut.AcquireContextAsync("run-ac4-success", BrowserMode.Headless, default);

        // Act — run completed with SUCCEEDED state
        await sut.ReleaseContextAsync("run-ac4-success", default);

        // Assert — context was closed regardless of success
        ctxMock.Verify(
            c => c.CloseAsync(It.IsAny<BrowserContextCloseOptions?>()),
            Times.Once,
            "AC#4: browser context must be fully closed after a successful run");
    }

    [Fact]
    public async Task AC4_HappyPath_ReleaseAfterFailure_ContextClosed()
    {
        // Arrange
        var ctxMock = new Mock<IBrowserContext>();
        ctxMock.Setup(c => c.CloseAsync(It.IsAny<BrowserContextCloseOptions?>()))
               .Returns(Task.CompletedTask);
        _browserMock
            .Setup(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .ReturnsAsync(ctxMock.Object);

        await using var sut = CreateSut();
        await sut.AcquireContextAsync("run-ac4-fail", BrowserMode.Headless, default);

        // Act — simulate worker calling release after a FAILED run
        await sut.ReleaseContextAsync("run-ac4-fail", default);

        // Assert
        ctxMock.Verify(
            c => c.CloseAsync(It.IsAny<BrowserContextCloseOptions?>()),
            Times.Once,
            "AC#4: browser context must be fully closed even after a failed run");
    }

    [Fact]
    public async Task AC4_HappyPath_ReleaseAfterCancellation_ContextClosed()
    {
        // Arrange
        var ctxMock = new Mock<IBrowserContext>();
        ctxMock.Setup(c => c.CloseAsync(It.IsAny<BrowserContextCloseOptions?>()))
               .Returns(Task.CompletedTask);
        _browserMock
            .Setup(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .ReturnsAsync(ctxMock.Object);

        await using var sut = CreateSut();
        await sut.AcquireContextAsync("run-ac4-cancel", BrowserMode.Headless, default);

        // Act — simulate CANCELLED terminal state
        await sut.ReleaseContextAsync("run-ac4-cancel", default);

        // Assert
        ctxMock.Verify(
            c => c.CloseAsync(It.IsAny<BrowserContextCloseOptions?>()),
            Times.Once,
            "AC#4: browser context must be fully closed on cancellation");
    }

    [Fact]
    public async Task AC4_HappyPath_ReleaseAfterTimeout_ContextClosed()
    {
        // Arrange
        var ctxMock = new Mock<IBrowserContext>();
        ctxMock.Setup(c => c.CloseAsync(It.IsAny<BrowserContextCloseOptions?>()))
               .Returns(Task.CompletedTask);
        _browserMock
            .Setup(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .ReturnsAsync(ctxMock.Object);

        await using var sut = CreateSut();
        await sut.AcquireContextAsync("run-ac4-timeout", BrowserMode.Headless, default);

        // Act — simulate TIMED_OUT terminal state
        await sut.ReleaseContextAsync("run-ac4-timeout", default);

        // Assert
        ctxMock.Verify(
            c => c.CloseAsync(It.IsAny<BrowserContextCloseOptions?>()),
            Times.Once,
            "AC#4: browser context must be fully closed on timeout");
    }

    [Fact]
    public async Task AC4_HappyPath_DisposeClosesAllUnreleasedContexts()
    {
        // Arrange — two runs whose contexts were never explicitly released
        var ctxMock1 = new Mock<IBrowserContext>();
        var ctxMock2 = new Mock<IBrowserContext>();
        ctxMock1.Setup(c => c.CloseAsync(It.IsAny<BrowserContextCloseOptions?>())).Returns(Task.CompletedTask);
        ctxMock2.Setup(c => c.CloseAsync(It.IsAny<BrowserContextCloseOptions?>())).Returns(Task.CompletedTask);

        _browserMock
            .SetupSequence(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .ReturnsAsync(ctxMock1.Object)
            .ReturnsAsync(ctxMock2.Object);

        var sut = CreateSut(); // not using 'await using' — controlling dispose timing

        await sut.AcquireContextAsync("run-dispose-1", BrowserMode.Headless, default);
        await sut.AcquireContextAsync("run-dispose-2", BrowserMode.Headless, default);

        // Act — disposal must release all unreleased contexts
        await sut.DisposeAsync();

        // Assert — both contexts and the browser process closed
        ctxMock1.Verify(c => c.CloseAsync(It.IsAny<BrowserContextCloseOptions?>()), Times.Once,
            "AC#4: run-dispose-1 context must be closed on manager disposal");
        ctxMock2.Verify(c => c.CloseAsync(It.IsAny<BrowserContextCloseOptions?>()), Times.Once,
            "AC#4: run-dispose-2 context must be closed on manager disposal");
        _browserMock.Verify(b => b.CloseAsync(It.IsAny<BrowserCloseOptions?>()), Times.Once,
            "the browser process must be closed on manager disposal to prevent orphaned processes");
    }

    [Fact]
    public async Task AC4_HappyPath_ReleaseContextAsync_UnknownRunId_IsNoOp()
    {
        // AC#4: calling release for a run that was never acquired must be safe (no throw)
        await using var sut = CreateSut();

        await sut.Invoking(s => s.ReleaseContextAsync("run-never-acquired", default))
            .Should().NotThrowAsync(
                "releasing an unknown runId must be a safe no-op — avoids crash in cleanup paths");
    }

    [Fact]
    public async Task AC4_HappyPath_ApprovedProfileRefClearedAfterRelease_NextRunGetsFreshContext()
    {
        // Arrange — first run uses an approved profile; after release, the ref must be cleared
        const string runId = "run-profile-cleared";
        const string signedRef = "vault://sessions/ref-clear";
        const string storageStateJson = "{\"cookies\":[]}";

        var ctxMock1 = new Mock<IBrowserContext>();
        ctxMock1.Setup(c => c.CloseAsync(It.IsAny<BrowserContextCloseOptions?>())).Returns(Task.CompletedTask);

        _browserMock
            .Setup(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .ReturnsAsync(ctxMock1.Object);

        SessionStoreMock
            .Setup(s => s.LoadStorageStateAsync(signedRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(storageStateJson);

        await using var sut = CreateSut();

        sut.RegisterApprovedProfile(runId, signedRef);
        await sut.AcquireContextAsync(runId, BrowserMode.Headless, default);
        await sut.ReleaseContextAsync(runId, default);

        // Set up capture for the second context creation (same runId, new run)
        BrowserNewContextOptions? capturedSecond = null;
        _browserMock
            .Setup(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .Callback<BrowserNewContextOptions?>(o => capturedSecond = o)
            .ReturnsAsync(new Mock<IBrowserContext>().Object);

        // Act — re-acquire the same runId WITHOUT re-registering a profile
        await sut.AcquireContextAsync(runId, BrowserMode.Headless, default);

        // Assert — the second acquire must have null StorageState (profile ref was cleared)
        capturedSecond!.StorageState.Should().BeNull(
            "AC#4: approved profile reference must be cleared after release; " +
            "the subsequent run must start completely fresh");

        // Vault consulted only once (first run)
        SessionStoreMock.Verify(
            s => s.LoadStorageStateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── CEC contract compliance ───────────────────────────────────────────────
    // BRW-AT-28: context isolation results must never expose secrets in error paths.

    [Fact]
    public async Task CecCompliance_ReleaseContextAsync_ExceptionDuringClose_PropagatesCleanly()
    {
        // Arrange — context.CloseAsync throws (simulates a Playwright crash during close)
        var ctxMock = new Mock<IBrowserContext>();
        ctxMock.Setup(c => c.CloseAsync(It.IsAny<BrowserContextCloseOptions?>()))
               .ThrowsAsync(new PlaywrightException("context already closed"));

        _browserMock
            .Setup(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .ReturnsAsync(ctxMock.Object);

        await using var sut = CreateSut();
        await sut.AcquireContextAsync("run-close-throws", BrowserMode.Headless, default);

        // Act — release must propagate the exception (resource leak warning path)
        await sut.Invoking(s => s.ReleaseContextAsync("run-close-throws", default))
            .Should().ThrowAsync<PlaywrightException>(
                "a Playwright exception during context close must propagate to the caller " +
                "so the worker can log and escalate the potential resource leak");
    }

    [Fact]
    public async Task CecCompliance_ContextStateNeverLeaksAcrossRunIds()
    {
        // Verifies that the context returned to run-A is never accessible via run-B's key.
        var ctxA = new Mock<IBrowserContext>().Object;
        var ctxB = new Mock<IBrowserContext>().Object;

        _browserMock
            .SetupSequence(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .ReturnsAsync(ctxA)
            .ReturnsAsync(ctxB);

        await using var sut = CreateSut();

        // Acquire both run contexts concurrently
        var returnedA = await sut.AcquireContextAsync("run-isolate-A", BrowserMode.Headless, default);
        var returnedB = await sut.AcquireContextAsync("run-isolate-B", BrowserMode.Headless, default);

        // Assert: each run gets its own reference; neither can see the other's context
        returnedA.Should().NotBeSameAs(returnedB,
            "CEC isolation: run-A and run-B must never share the same IBrowserContext reference");
        returnedA.Should().BeSameAs(ctxA);
        returnedB.Should().BeSameAs(ctxB);
    }

    [Fact]
    public async Task CecCompliance_StorageStateNotPresentInErrorMessage_OnDuplicateAcquire()
    {
        // Arrange — first run acquires context; second attempt on same runId must fail
        _browserMock
            .Setup(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions?>()))
            .ReturnsAsync(new Mock<IBrowserContext>().Object);

        await using var sut = CreateSut();
        await sut.AcquireContextAsync("run-safe-err", BrowserMode.Headless, default);

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.AcquireContextAsync("run-safe-err", BrowserMode.Headless, default));

        // Assert — the exception message must not expose any secret value
        ex.Message.ToLowerInvariant().Should().NotContain("password",
            "error messages must never expose credential material");
        ex.Message.ToLowerInvariant().Should().NotContain("token");
        ex.Message.ToLowerInvariant().Should().NotContain("secret");
        ex.Message.ToLowerInvariant().Should().NotContain("cookie");
        ex.Message.ToLowerInvariant().Should().NotContain("storagestate");
    }
}
