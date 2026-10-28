using FluentAssertions;
using Moq;
using TractoRuntime.Browser.Capabilities;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Mode;
using TractoRuntime.Browser.Tests.Fixtures;

namespace TractoRuntime.Browser.Tests;

/// <summary>
/// Story-level acceptance tests for BRW-US-002 — Headless and Headed Execution Mode Selection.
/// Acceptance tests: BRW-AT-10 (headless smoke), BRW-AT-22 (headed exclusion / session check).
/// CEC authority: BRW-002, BRW-006.
///
/// Coverage map:
///   AC#1 — headless worker completes without a desktop session
///   AC#2 — headed worker requires authorized active Windows interactive session
///   AC#3 — headed Browser worker and Desktop worker cannot simultaneously hold ownership
///   AC#4 — runtime reports mode and interactiveSessionId in capability advertisement
///   AC#5 — isolated-host supervisor enforces the foreground-input exclusion rule (in-process contract)
/// </summary>
public sealed class BRW_US_002_Tests : BrowserTestBase
{
    // ── Shared mocks / SUT instances ─────────────────────────────────────────

    private readonly Mock<IWindowsSessionInspector> _sessionInspector = new();
    private readonly Mock<IRuntimeInfo> _runtimeInfo = new();

    private ModeSelector BuildModeSelector() => new(_sessionInspector.Object);

    private CapabilityAdvertiser BuildCapabilityAdvertiser()
    {
        _runtimeInfo
            .Setup(r => r.GetBrowserInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(("chromium", "1.48.0"));
        return new CapabilityAdvertiser(_runtimeInfo.Object);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC#1 — Headless mode: no Windows desktop session required
    // BRW-AT-10
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AC1_HappyPath_HeadlessScript_ModeConfigIsHeadless()
    {
        // A script declared as Headless must produce a headless ExecutionModeConfig.
        var script = TestArtifactFactory.MakeScript(BrowserMode.Headless);
        var sut = BuildModeSelector();

        var config = sut.SelectMode(script);

        config.Mode.Should().Be(BrowserMode.Headless,
            "AC#1: headless script must select headless mode");
        config.RequiresActiveSession.Should().BeFalse(
            "AC#1: headless does not require an active Windows session");
        config.InteractiveSessionId.Should().BeNull(
            "AC#1: no session ID is allocated for a headless worker");
    }

    [Fact]
    public async Task AC1_HappyPath_HeadlessValidation_SucceedsWithoutSessionInspection()
    {
        // ValidateModeAsync must not call the session inspector for headless mode.
        var config = new ExecutionModeConfig { Mode = BrowserMode.Headless };
        var sut = BuildModeSelector();

        var result = await sut.ValidateModeAsync(config, CancellationToken.None);

        result.Mode.Should().Be(BrowserMode.Headless);
        result.InteractiveSessionId.Should().BeNull();

        _sessionInspector.Verify(
            s => s.GetActiveInteractiveSessionIdAsync(It.IsAny<CancellationToken>()),
            Times.Never,
            "AC#1: headless validation must never query the Windows session");
    }

    [Fact]
    public async Task AC1_HappyPath_HeadlessWorker_SucceedsOnHostWithNoInteractiveSession()
    {
        // A headless worker must not fail even when no Windows user is logged in.
        _sessionInspector
            .Setup(s => s.GetActiveInteractiveSessionIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var config = new ExecutionModeConfig { Mode = BrowserMode.Headless };
        var sut = BuildModeSelector();

        var act = async () => await sut.ValidateModeAsync(config, CancellationToken.None);

        await act.Should().NotThrowAsync(
            "AC#1: a headless worker does not require a logged-in Windows session");
    }

    [Fact]
    public async Task AC1_CecCompliance_HeadlessResult_HasNoErrorCategory()
    {
        // When headless validation succeeds, no CEC error is produced.
        var config = new ExecutionModeConfig { Mode = BrowserMode.Headless };
        var sut = BuildModeSelector();

        var result = await sut.ValidateModeAsync(config, CancellationToken.None);

        // No exception thrown means no CEC error envelope is emitted.
        result.Should().NotBeNull("AC#1: successful headless validation returns a config, not an error");
        result.Mode.Should().Be(BrowserMode.Headless);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC#2 — Headed mode: requires authorized active Windows interactive session
    // BRW-AT-22
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AC2_HappyPath_HeadedScript_ModeConfigIsHeaded()
    {
        // A script declared as Headed must produce a headed ExecutionModeConfig.
        var script = TestArtifactFactory.MakeScript(BrowserMode.Headed);
        var sut = BuildModeSelector();

        var config = sut.SelectMode(script);

        config.Mode.Should().Be(BrowserMode.Headed,
            "AC#2: headed script must select headed mode");
        config.RequiresActiveSession.Should().BeTrue(
            "AC#2: headed mode always requires an active Windows session");
        config.InteractiveSessionId.Should().BeNull(
            "session ID is deferred to ValidateModeAsync — not resolved at SelectMode time");
    }

    [Fact]
    public async Task AC2_HappyPath_HeadedValidation_WithActiveSession_ReturnsSessionId()
    {
        // When a Windows interactive session is available, the headed worker obtains it.
        const string session = "1";
        _sessionInspector
            .Setup(s => s.GetActiveInteractiveSessionIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var config = new ExecutionModeConfig { Mode = BrowserMode.Headed };
        var sut = BuildModeSelector();

        var result = await sut.ValidateModeAsync(config, CancellationToken.None);

        result.Mode.Should().Be(BrowserMode.Headed);
        result.InteractiveSessionId.Should().Be(session,
            "AC#2: headed validation must resolve and return the active Windows session ID");
    }

    [Fact]
    public async Task AC2_Negative_HeadedValidation_WithNoSession_ThrowsSessionUnavailable()
    {
        // When no Windows session is active, headed validation must fail with SESSION_UNAVAILABLE.
        _sessionInspector
            .Setup(s => s.GetActiveInteractiveSessionIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var config = new ExecutionModeConfig { Mode = BrowserMode.Headed };
        var sut = BuildModeSelector();

        var act = async () => await sut.ValidateModeAsync(config, CancellationToken.None);

        await act.Should().ThrowAsync<ModeValidationException>()
            .Where(ex => ex.Mode == BrowserMode.Headed &&
                         ex.ErrorCategory == ErrorCategory.SESSION_UNAVAILABLE,
                   "AC#2: no interactive session → errorCategory=SESSION_UNAVAILABLE per CEC §6.2");
    }

    [Fact]
    public async Task AC2_CecCompliance_HeadedNoSession_ErrorCategoryIsSessionUnavailable()
    {
        // Verify the CEC error category is exactly SESSION_UNAVAILABLE (not INTERNAL or ENVIRONMENT).
        _sessionInspector
            .Setup(s => s.GetActiveInteractiveSessionIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var config = new ExecutionModeConfig { Mode = BrowserMode.Headed };
        var sut = BuildModeSelector();

        var ex = await Record.ExceptionAsync(
            () => sut.ValidateModeAsync(config, CancellationToken.None));

        ex.Should().BeOfType<ModeValidationException>();
        var mve = (ModeValidationException)ex!;
        mve.ErrorCategory.Should().Be(ErrorCategory.SESSION_UNAVAILABLE,
            "CEC §6.2 requires SESSION_UNAVAILABLE when the desktop session is absent");
    }

    [Fact]
    public async Task AC2_Negative_HeadedValidation_Cancellation_ThrowsOperationCancelled()
    {
        // A cancellation token must propagate correctly through headed session validation.
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _sessionInspector
            .Setup(s => s.GetActiveInteractiveSessionIdAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var config = new ExecutionModeConfig { Mode = BrowserMode.Headed };
        var sut = BuildModeSelector();

        var act = async () => await sut.ValidateModeAsync(config, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "cancellation must propagate when the session inspector is cancelled");
    }

    [Fact]
    public async Task AC2_Negative_HeadedValidation_InspectorThrows_PropagatesException()
    {
        // An unexpected exception from the session inspector must surface, not be swallowed.
        _sessionInspector
            .Setup(s => s.GetActiveInteractiveSessionIdAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("WTS API unavailable"));

        var config = new ExecutionModeConfig { Mode = BrowserMode.Headed };
        var sut = BuildModeSelector();

        var act = async () => await sut.ValidateModeAsync(config, CancellationToken.None);

        await act.Should().ThrowAsync<Exception>(
            "exceptions from the session inspector must propagate to the caller");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC#3 — Mutual exclusion: Browser and Desktop workers cannot share a session
    // BRW-AT-22
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AC3_HappyPath_FirstHeadedWorker_AcquiresForegroundOwnership()
    {
        // The first headed worker for a session acquires foreground-input ownership.
        await using var sut = new ForegroundInputOwner();
        const string session = "session-AC3-first";
        const string runId = "run-AC3-001";

        var result = await sut.TryAcquireAsync(runId, session, CancellationToken.None);

        result.Acquired.Should().BeTrue(
            "AC#3: the first headed worker must acquire foreground-input ownership");
        result.SessionId.Should().Be(session);
        result.ConflictingRunId.Should().BeNull("no conflict for the first acquisition");

        await sut.ReleaseAsync(runId, session, CancellationToken.None);
    }

    [Fact]
    public async Task AC3_Negative_SecondHeadedWorker_SameSession_IsRejected()
    {
        // A second headed worker targeting the same session must be rejected immediately.
        await using var sut = new ForegroundInputOwner();
        const string session = "session-AC3-conflict";
        const string firstRun = "run-AC3-browser-001";
        const string secondRun = "run-AC3-desktop-001"; // simulates Desktop worker

        await sut.TryAcquireAsync(firstRun, session, CancellationToken.None);

        var result = await sut.TryAcquireAsync(secondRun, session, CancellationToken.None);

        result.Acquired.Should().BeFalse(
            "AC#3: second headed/Desktop attempt for the same session must be rejected");
        result.ConflictingRunId.Should().Be(firstRun,
            "the conflicting run ID must be reported for diagnostics");
        result.SessionId.Should().Be(session);

        await sut.ReleaseAsync(firstRun, session, CancellationToken.None);
    }

    [Fact]
    public async Task AC3_HappyPath_TwoDifferentSessions_DoNotConflict()
    {
        // Workers targeting distinct Windows sessions are fully independent.
        await using var sut = new ForegroundInputOwner();
        const string sessionA = "session-AC3-A";
        const string sessionB = "session-AC3-B";
        const string runA = "run-AC3-A";
        const string runB = "run-AC3-B";

        var resultA = await sut.TryAcquireAsync(runA, sessionA, CancellationToken.None);
        var resultB = await sut.TryAcquireAsync(runB, sessionB, CancellationToken.None);

        resultA.Acquired.Should().BeTrue("session A is independent");
        resultB.Acquired.Should().BeTrue("session B is independent — no cross-session conflict");

        await sut.ReleaseAsync(runA, sessionA, CancellationToken.None);
        await sut.ReleaseAsync(runB, sessionB, CancellationToken.None);
    }

    [Fact]
    public async Task AC3_HappyPath_AfterRelease_NewWorkerCanAcquire()
    {
        // Once the first worker releases ownership, a subsequent worker can acquire it.
        await using var sut = new ForegroundInputOwner();
        const string session = "session-AC3-release";
        const string firstRun = "run-AC3-release-001";
        const string secondRun = "run-AC3-release-002";

        await sut.TryAcquireAsync(firstRun, session, CancellationToken.None);
        await sut.ReleaseAsync(firstRun, session, CancellationToken.None);

        var result = await sut.TryAcquireAsync(secondRun, session, CancellationToken.None);

        result.Acquired.Should().BeTrue(
            "AC#3: after the first owner releases, the next worker must be able to acquire");

        await sut.ReleaseAsync(secondRun, session, CancellationToken.None);
    }

    [Fact]
    public async Task AC3_Negative_NonOwner_CannotReleaseOwnershipBelongingToAnother()
    {
        // A non-owner release must be a no-op; the original owner retains ownership.
        await using var sut = new ForegroundInputOwner();
        const string session = "session-AC3-norelease";
        const string owner = "run-AC3-owner";
        const string nonOwner = "run-AC3-intruder";

        await sut.TryAcquireAsync(owner, session, CancellationToken.None);
        await sut.ReleaseAsync(nonOwner, session, CancellationToken.None); // no-op

        sut.GetCurrentOwner(session).Should().Be(owner,
            "AC#3: only the owning run can release foreground-input ownership");

        await sut.ReleaseAsync(owner, session, CancellationToken.None);
    }

    [Fact]
    public async Task AC3_Negative_SimulatedDesktopWorkerConflict_IsRejected()
    {
        // Simulates the AC#3 scenario: Browser headed worker holds session, Desktop worker attempts.
        // The Desktop worker is represented by a different runId; the underlying contract is identical.
        await using var sut = new ForegroundInputOwner();
        const string session = "session-AC3-desktop-conflict";
        const string browserRun = "run-browser-headed-01";
        const string desktopRun = "run-desktop-01";

        var browserResult = await sut.TryAcquireAsync(browserRun, session, CancellationToken.None);
        browserResult.Acquired.Should().BeTrue("browser headed worker acquires first");

        var desktopResult = await sut.TryAcquireAsync(desktopRun, session, CancellationToken.None);
        desktopResult.Acquired.Should().BeFalse(
            "AC#3: Desktop worker must be rejected when a headed Browser worker owns the session");
        desktopResult.ConflictingRunId.Should().Be(browserRun);

        await sut.ReleaseAsync(browserRun, session, CancellationToken.None);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC#4 — Capability advertisement: mode and interactiveSessionId reported
    // BRW-AT-10 (headless payload), BRW-AT-22 (headed payload)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AC4_HappyPath_HeadlessPayload_ReportsModeAndNullSessionId()
    {
        // Headless capability payload must contain mode="headless" and no interactiveSessionId.
        var sut = BuildCapabilityAdvertiser();
        var config = new ExecutionModeConfig { Mode = BrowserMode.Headless };

        var payload = await sut.BuildPayloadAsync("worker-h1", config, CancellationToken.None);

        payload.Mode.Should().Be("headless",
            "AC#4: capability advertisement must report mode=headless");
        payload.InteractiveSessionId.Should().BeNull(
            "AC#4: headless advertisement must not include an interactiveSessionId");
        payload.RuntimeType.Should().Be("browser",
            "capability payload must always carry runtimeType=browser");
    }

    [Fact]
    public async Task AC4_HappyPath_HeadlessPayload_CapabilitiesContainHeadlessToken()
    {
        // Headless capability list must include "browser.headless" and engine token.
        var sut = BuildCapabilityAdvertiser();
        var config = new ExecutionModeConfig { Mode = BrowserMode.Headless };

        var payload = await sut.BuildPayloadAsync("worker-h2", config, CancellationToken.None);

        payload.Capabilities.Should().Contain("browser.headless",
            "AC#4: headless workers must advertise the browser.headless capability");
        payload.Capabilities.Should().Contain("browser.engine.chromium",
            "BRW-006: browser engine must be included in capability tokens");
        payload.Capabilities.Should().NotContain(c => c.StartsWith("browser.session."),
            "AC#4: headless workers must not advertise a session token");
    }

    [Fact]
    public async Task AC4_HappyPath_HeadedPayload_ReportsModeAndSessionId()
    {
        // Headed capability payload must carry mode="headed" and the resolved interactiveSessionId.
        var sut = BuildCapabilityAdvertiser();
        var config = new ExecutionModeConfig { Mode = BrowserMode.Headed, InteractiveSessionId = "2" };

        var payload = await sut.BuildPayloadAsync("worker-hd1", config, CancellationToken.None);

        payload.Mode.Should().Be("headed",
            "AC#4: capability advertisement must report mode=headed");
        payload.InteractiveSessionId.Should().Be("2",
            "AC#4: headed advertisement must include the resolved interactiveSessionId");
    }

    [Fact]
    public async Task AC4_HappyPath_HeadedPayload_CapabilitiesContainHeadedAndSessionToken()
    {
        // Headed capability list must include "browser.headed" and "browser.session.<id>".
        var sut = BuildCapabilityAdvertiser();
        var config = new ExecutionModeConfig { Mode = BrowserMode.Headed, InteractiveSessionId = "3" };

        var payload = await sut.BuildPayloadAsync("worker-hd2", config, CancellationToken.None);

        payload.Capabilities.Should().Contain("browser.headed",
            "AC#4: headed workers must advertise the browser.headed capability");
        payload.Capabilities.Should().Contain("browser.session.3",
            "AC#4: headed workers must advertise a session token for the acquired session");
        payload.Capabilities.Should().Contain("browser.engine.chromium",
            "BRW-006: browser engine must always be in the capability list");
    }

    [Fact]
    public async Task AC4_HappyPath_HeadedPayload_WorkerIdAndRuntimeTypeAreSet()
    {
        // The Orchestrator uses workerId and runtimeType to route jobs.
        var sut = BuildCapabilityAdvertiser();
        var config = new ExecutionModeConfig { Mode = BrowserMode.Headed, InteractiveSessionId = "1" };

        var payload = await sut.BuildPayloadAsync("worker-X42", config, CancellationToken.None);

        payload.WorkerId.Should().Be("worker-X42");
        payload.RuntimeType.Should().Be("browser");
    }

    [Fact]
    public async Task AC4_HappyPath_BrowserEngineAndVersion_AreIncluded()
    {
        // BRW-006: browser engine and version must be explicitly reported.
        var sut = BuildCapabilityAdvertiser();
        var config = new ExecutionModeConfig { Mode = BrowserMode.Headless };

        var payload = await sut.BuildPayloadAsync("worker-meta", config, CancellationToken.None);

        payload.BrowserEngine.Should().Be("chromium",
            "BRW-006: engine must be reported in the capability advertisement");
        payload.BrowserVersion.Should().Be("1.48.0",
            "BRW-006: version must be reported for environment reproducibility");
    }

    [Fact]
    public async Task AC4_HappyPath_CapabilityDigest_IsNonEmptyAndDeterministic()
    {
        // The capability digest is a non-empty hex string and is stable for the same inputs.
        var sut = BuildCapabilityAdvertiser();
        var config = new ExecutionModeConfig { Mode = BrowserMode.Headless };

        var p1 = await sut.BuildPayloadAsync("worker-d1", config, CancellationToken.None);
        var p2 = await sut.BuildPayloadAsync("worker-d1", config, CancellationToken.None);

        p1.CapabilityDigest.Should().NotBeNullOrEmpty("BRW-006: capabilityDigest must be present");
        p1.CapabilityDigest!.Should().MatchRegex("^[0-9a-f]+$",
            "capabilityDigest must be a lowercase hex string");
        p1.CapabilityDigest.Should().Be(p2.CapabilityDigest,
            "capabilityDigest must be deterministic (integrity hint, not timestamp-based)");
    }

    [Fact]
    public async Task AC4_Negative_HeadedPayload_NullSessionId_NoSessionTokenInCapabilities()
    {
        // If the session ID is not yet resolved (pre-validation edge case), no session token
        // must appear in the capability list.
        var sut = BuildCapabilityAdvertiser();
        var config = new ExecutionModeConfig { Mode = BrowserMode.Headed, InteractiveSessionId = null };

        var payload = await sut.BuildPayloadAsync("worker-null-session", config, CancellationToken.None);

        payload.Capabilities.Should().NotContain(c => c.StartsWith("browser.session."),
            "AC#4: no session token may appear when the session ID is unresolved");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC#5 — Isolated-host: in-process foreground-input exclusion contract
    // (The host-level supervisor coordinates cross-process; here we verify the
    //  in-process IForegroundInputOwner contract that the supervisor relies on.)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AC5_IsolatedHost_ForegroundInputOwner_ExclusionIsAtomic()
    {
        // The in-process owner must reject the second acquisition atomically (no TOCTOU gap).
        // This is the contract the supervisor calls to coordinate headed workers on a host.
        await using var sut = new ForegroundInputOwner();
        const string session = "session-AC5-isolated";
        const string runA = "run-AC5-browser";
        const string runB = "run-AC5-desktop-isolation";

        var resultA = await sut.TryAcquireAsync(runA, session, CancellationToken.None);
        var resultB = await sut.TryAcquireAsync(runB, session, CancellationToken.None);

        resultA.Acquired.Should().BeTrue();
        resultB.Acquired.Should().BeFalse(
            "AC#5: in-process exclusion must be enforced atomically; the second attempt is rejected");
        resultB.ConflictingRunId.Should().NotBeNullOrEmpty(
            "AC#5: the conflicting owner must be identified for supervisor coordination");

        await sut.ReleaseAsync(runA, session, CancellationToken.None);
    }

    [Fact]
    public async Task AC5_IsolatedHost_NoOwner_ReturnsNull()
    {
        // The supervisor uses GetCurrentOwner to check host state before coordinating.
        await using var sut = new ForegroundInputOwner();
        const string fresh = "session-AC5-unowned";

        var owner = sut.GetCurrentOwner(fresh);

        owner.Should().BeNull(
            "AC#5: a session with no headed worker has no owner; supervisor sees null");

        await Task.CompletedTask;
    }

    [Fact]
    public async Task AC5_IsolatedHost_CurrentOwner_ReflectsAcquisition()
    {
        // After acquisition, GetCurrentOwner must return the owning runId so the supervisor
        // can correctly report the exclusive holder to the Orchestrator.
        await using var sut = new ForegroundInputOwner();
        const string session = "session-AC5-owner-check";
        const string runId = "run-AC5-owner";

        await sut.TryAcquireAsync(runId, session, CancellationToken.None);

        sut.GetCurrentOwner(session).Should().Be(runId,
            "AC#5: GetCurrentOwner must return the active owner so the supervisor can report it");

        await sut.ReleaseAsync(runId, session, CancellationToken.None);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // CEC contract compliance — end-to-end mode → ownership → advertisement flow
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CecCompliance_HeadlessFlow_ProducesCorrectCapabilityPayload()
    {
        // Full headless flow: SelectMode → ValidateMode → BuildPayload.
        // The Orchestrator receives a payload with mode=headless, no sessionId, correct engine.
        var modeSelector = BuildModeSelector();
        var advertiser = BuildCapabilityAdvertiser();

        var script = TestArtifactFactory.MakeScript(BrowserMode.Headless);
        var config = modeSelector.SelectMode(script);
        var validated = await modeSelector.ValidateModeAsync(config, CancellationToken.None);
        var payload = await advertiser.BuildPayloadAsync("w-cec-hl", validated, CancellationToken.None);

        payload.Mode.Should().Be("headless");
        payload.InteractiveSessionId.Should().BeNull();
        payload.RuntimeType.Should().Be("browser");
        payload.BrowserEngine.Should().NotBeNullOrEmpty();
        payload.CapabilityDigest.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CecCompliance_HeadedFlow_ProducesCorrectCapabilityPayload()
    {
        // Full headed flow: SelectMode → ValidateMode → AcquireForeground → BuildPayload.
        const string session = "2";
        _sessionInspector
            .Setup(s => s.GetActiveInteractiveSessionIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var modeSelector = BuildModeSelector();
        var advertiser = BuildCapabilityAdvertiser();

        var script = TestArtifactFactory.MakeScript(BrowserMode.Headed);
        var config = modeSelector.SelectMode(script);
        var validated = await modeSelector.ValidateModeAsync(config, CancellationToken.None);

        await using var owner = new ForegroundInputOwner();
        var ownership = await owner.TryAcquireAsync("run-cec-hd", validated.InteractiveSessionId!, CancellationToken.None);
        ownership.Acquired.Should().BeTrue();

        var payload = await advertiser.BuildPayloadAsync("w-cec-hd", validated, CancellationToken.None);

        payload.Mode.Should().Be("headed");
        payload.InteractiveSessionId.Should().Be(session,
            "CEC compliance: headed payload must carry the resolved interactiveSessionId");
        payload.RuntimeType.Should().Be("browser");
        payload.Capabilities.Should().Contain($"browser.session.{session}");

        await owner.ReleaseAsync("run-cec-hd", session, CancellationToken.None);
    }

    [Fact]
    public async Task CecCompliance_HeadedNoSession_ErrorCategoryMapsToSessionUnavailable()
    {
        // When headed validation fails, the ModeValidationException carries the exact
        // CEC §6.2 error category SESSION_UNAVAILABLE to be wrapped into CecError.
        _sessionInspector
            .Setup(s => s.GetActiveInteractiveSessionIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var modeSelector = BuildModeSelector();
        var config = new ExecutionModeConfig { Mode = BrowserMode.Headed };

        ModeValidationException? caught = null;
        try
        {
            await modeSelector.ValidateModeAsync(config, CancellationToken.None);
        }
        catch (ModeValidationException ex)
        {
            caught = ex;
        }

        caught.Should().NotBeNull("headed mode with no session must throw ModeValidationException");
        caught!.ErrorCategory.Should().Be(ErrorCategory.SESSION_UNAVAILABLE,
            "CEC §6.2: the SESSION_UNAVAILABLE category is required for missing desktop sessions");
        caught.Mode.Should().Be(BrowserMode.Headed);

        // Simulate building the CEC run result that the worker would report.
        var runResult = TestArtifactFactory.MakeFailedResult(
            "run-cec-session-fail",
            caught.ErrorCategory,
            caught.Message);

        runResult.State.Should().Be(RunState.FAILED,
            "CEC: a mode validation failure produces run state FAILED");
        runResult.Error!.Category.Should().Be(ErrorCategory.SESSION_UNAVAILABLE,
            "CEC: errorCategory in the run result must match the ModeValidationException");
    }

    [Fact]
    public async Task CecCompliance_MockOrchestratorReceivesHeadlessRegistration()
    {
        // The Orchestrator mock accepts a registration that reports headless capabilities.
        var advertiser = BuildCapabilityAdvertiser();
        var config = new ExecutionModeConfig { Mode = BrowserMode.Headless };
        var payload = await advertiser.BuildPayloadAsync("worker-reg-hl", config, CancellationToken.None);

        // Map WorkerCapabilityPayload → WorkerRegistration (as the worker would do before dispatch).
        var registration = new WorkerRegistration
        {
            WorkerId = payload.WorkerId,
            RuntimeType = payload.RuntimeType,
            Capabilities = payload.Capabilities,
            CapabilityDigest = payload.CapabilityDigest
        };

        Orchestrator.RegisterWorker(registration);

        Orchestrator.RegisteredWorkers.Should().ContainKey("worker-reg-hl");
        var reg = Orchestrator.RegisteredWorkers["worker-reg-hl"];
        reg.RuntimeType.Should().Be("browser");
        reg.Capabilities.Should().Contain("browser.headless");
        reg.CapabilityDigest.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CecCompliance_MockOrchestratorReceivesHeadedRegistration()
    {
        // The Orchestrator mock accepts a headed registration with session token.
        const string session = "4";
        var advertiser = BuildCapabilityAdvertiser();
        var config = new ExecutionModeConfig { Mode = BrowserMode.Headed, InteractiveSessionId = session };
        var payload = await advertiser.BuildPayloadAsync("worker-reg-hd", config, CancellationToken.None);

        var registration = new WorkerRegistration
        {
            WorkerId = payload.WorkerId,
            RuntimeType = payload.RuntimeType,
            Capabilities = payload.Capabilities,
            CapabilityDigest = payload.CapabilityDigest
        };

        Orchestrator.RegisterWorker(registration);

        var reg = Orchestrator.RegisteredWorkers["worker-reg-hd"];
        reg.Capabilities.Should().Contain("browser.headed");
        reg.Capabilities.Should().Contain($"browser.session.{session}",
            "Orchestrator registration must carry the session token for headed workers");
    }

    [Fact]
    public async Task CecCompliance_RunResultForFailedModeValidation_HasCorrectFields()
    {
        // The run result reported after a mode validation failure must satisfy the CEC contract:
        // state=FAILED, error.category=SESSION_UNAVAILABLE, endedAt is set.
        var result = TestArtifactFactory.MakeFailedResult(
            "run-mode-fail-001",
            ErrorCategory.SESSION_UNAVAILABLE,
            "No active Windows interactive session for headed mode");

        result.State.Should().Be(RunState.FAILED,
            "CEC: mode validation failure is a terminal FAILED state");
        result.Error.Should().NotBeNull();
        result.Error!.Category.Should().Be(ErrorCategory.SESSION_UNAVAILABLE);
        result.Error.Message.Should().NotBeNullOrEmpty();
        result.EndedAt.Should().NotBeNull("CEC: terminal states must have endedAt set");
        result.StartedAt.Should().BeBefore(result.EndedAt!.Value);

        await Task.CompletedTask;
    }
}
