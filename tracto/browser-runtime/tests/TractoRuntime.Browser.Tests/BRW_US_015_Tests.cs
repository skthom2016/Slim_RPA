using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Moq;
using TractoRuntime.Browser.Actions;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Failure;
using TractoRuntime.Browser.Locators;
using TractoRuntime.Browser.Network;
using TractoRuntime.Browser.Service;
using TractoRuntime.Browser.Tests.Fixtures;

namespace TractoRuntime.Browser.Tests;

/// <summary>
/// Acceptance tests for BRW-US-015 — Network Failure Classification and Uncertain Outcome Handling.
/// Covers BRW-AT-08, BRW-AT-11, BRW-AT-12, BRW-AT-20, BRW-AT-23, BRW-AT-24, BRW-AT-25.
/// Requirement refs: BRE-003, BRE-007, BRE-008.
/// </summary>
/// <remarks>
/// AC#1 — DNS failure, TLS error, proxy failure, or HTTP error is classified with
///         errorCategory=TRANSPORT and an appropriate browser.* detail code.
/// AC#2 — Connection drop during a form submission that cannot be proven to have succeeded
///         or failed terminates the attempt with run state UNKNOWN — no automatic retry.
/// AC#3 — When a registered service returns serviceOutcome=UNKNOWN_OUTCOME, the attempt
///         terminates as UNKNOWN; no actionCode branch is followed.
/// AC#4 — When a registered service returns a definitive outcome (SUCCESS, BUSINESS_REJECTION,
///         TECHNICAL_FAILURE, TRANSIENT_FAILURE), the configured actionCode branch may proceed.
/// AC#5 — Attempt journal persistence prevents duplicate submission after Orchestrator restart.
/// </remarks>
public sealed class BRW_US_015_Tests : BrowserTestBase
{
    // ────────────────────────────────────────────────────────────────────────────
    // AC#1 — Transport failure classification (BRE-003 / BRW-US-015 AC#1)
    // BRW-AT-08, BRW-AT-20
    // ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("net::ERR_NAME_NOT_RESOLVED at https://example.com", "browser.dnsFailure")]
    [InlineData("net::ERR_DNS_TIMED_OUT at https://example.com",     "browser.dnsFailure")]
    public void AC1_PlaywrightException_DnsFailure_ClassifiedAsTransportDnsFailure(
        string playwrightMessage, string expectedDetailCode)
    {
        // Arrange
        var sut = new FailureClassifier(NullLogger<FailureClassifier>.Instance);
        var ex  = new PlaywrightException(playwrightMessage);

        // Act
        var result = sut.Classify(ex, stateKnown: false);

        // Assert
        result.Category.Should().Be(ErrorCategory.TRANSPORT,
            "DNS resolution failures must map to TRANSPORT (BRE-003 / AC#1)");
        result.DetailCode.Should().Be(expectedDetailCode);
    }

    [Theory]
    [InlineData("net::ERR_CERT_AUTHORITY_INVALID at https://self-signed.example.com",  "browser.tlsCertificateError")]
    [InlineData("net::ERR_CERT_DATE_INVALID at https://expired.example.com",           "browser.tlsCertificateError")]
    [InlineData("net::ERR_CERT_COMMON_NAME_INVALID at https://mismatch.example.com",   "browser.tlsCertificateError")]
    [InlineData("net::ERR_SSL_PROTOCOL_ERROR at https://tls.example.com",              "browser.tlsCertificateError")]
    public void AC1_PlaywrightException_TlsError_ClassifiedAsTransportTlsError(
        string playwrightMessage, string expectedDetailCode)
    {
        // Arrange
        var sut = new FailureClassifier(NullLogger<FailureClassifier>.Instance);
        var ex  = new PlaywrightException(playwrightMessage);

        // Act
        var result = sut.Classify(ex, stateKnown: false);

        // Assert
        result.Category.Should().Be(ErrorCategory.TRANSPORT,
            "TLS / certificate errors must map to TRANSPORT (BRW-US-005 AC#6 / BRW-US-015 AC#1)");
        result.DetailCode.Should().Be(expectedDetailCode);
    }

    [Theory]
    [InlineData("net::ERR_PROXY_CONNECTION_FAILED at https://example.com", "browser.proxyFailure")]
    [InlineData("net::ERR_TUNNEL_CONNECTION_FAILED at https://example.com","browser.proxyFailure")]
    [InlineData("net::ERR_SOCKS_CONNECTION_FAILED at https://example.com", "browser.proxyFailure")]
    public void AC1_PlaywrightException_ProxyFailure_ClassifiedAsTransportProxyFailure(
        string playwrightMessage, string expectedDetailCode)
    {
        // Arrange
        var sut = new FailureClassifier(NullLogger<FailureClassifier>.Instance);
        var ex  = new PlaywrightException(playwrightMessage);

        // Act
        var result = sut.Classify(ex, stateKnown: false);

        // Assert
        result.Category.Should().Be(ErrorCategory.TRANSPORT,
            "Proxy failures must map to TRANSPORT (BRW-US-008 AC#7 / BRW-US-015 AC#1)");
        result.DetailCode.Should().Be(expectedDetailCode);
    }

    [Theory]
    [InlineData("net::ERR_CONNECTION_REFUSED at https://example.com",   "browser.networkOffline")]
    [InlineData("net::ERR_CONNECTION_RESET at https://example.com",     "browser.networkOffline")]
    [InlineData("net::ERR_INTERNET_DISCONNECTED at https://example.com","browser.networkOffline")]
    [InlineData("net::ERR_CONNECTION_CLOSED at https://example.com",    "browser.networkOffline")]
    [InlineData("net::ERR_EMPTY_RESPONSE at https://example.com",       "browser.networkOffline")]
    public void AC1_PlaywrightException_ConnectionDrop_ClassifiedAsTransportNetworkOffline(
        string playwrightMessage, string expectedDetailCode)
    {
        // Arrange — stateKnown=false because the action outcome is uncertain
        var sut = new FailureClassifier(NullLogger<FailureClassifier>.Instance);
        var ex  = new PlaywrightException(playwrightMessage);

        // Act
        var result = sut.Classify(ex, stateKnown: false);

        // Assert
        result.Category.Should().Be(ErrorCategory.TRANSPORT,
            "Connection drops must map to TRANSPORT; caller uses stateKnown=false to " +
            "decide FAILED vs UNKNOWN (BRW-US-015 AC#2)");
        result.DetailCode.Should().Be(expectedDetailCode);
    }

    [Fact]
    public void AC1_NetworkTransportException_PreClassified_MapsToTransport()
    {
        // Arrange — NetworkTransportException is thrown by NavigationExecutor when it
        // detects a connection drop during a non-idempotent submit action.
        var sut = new FailureClassifier(NullLogger<FailureClassifier>.Instance);
        var ex  = new NetworkTransportException("browser.networkOffline",
            "Connection reset during form submit — outcome uncertain (BRW-US-015 AC#2).");

        // Act
        var result = sut.Classify(ex, stateKnown: false);

        // Assert
        result.Category.Should().Be(ErrorCategory.TRANSPORT);
        result.DetailCode.Should().Be("browser.networkOffline");
    }

    [Fact]
    public void AC1_NetworkTransportException_WithTlsDetailCode_MapsCorrectly()
    {
        var sut = new FailureClassifier(NullLogger<FailureClassifier>.Instance);
        var ex  = new NetworkTransportException("browser.tlsCertificateError",
            "TLS certificate error detected (BRW-US-005 AC#6).");

        var result = sut.Classify(ex, stateKnown: false);

        result.Category.Should().Be(ErrorCategory.TRANSPORT);
        result.DetailCode.Should().Be("browser.tlsCertificateError");
    }

    // ── Non-transport errors must not be mis-classified ──────────────────────

    [Fact]
    public void AC1_NavigationTimeout_IsNotClassifiedAsTransport()
    {
        var sut = new FailureClassifier(NullLogger<FailureClassifier>.Instance);
        var ex  = new PlaywrightException(
            "page.goto: Timeout 30000ms exceeded. Call log: ...");

        var result = sut.Classify(ex, stateKnown: true);

        result.Category.Should().Be(ErrorCategory.TIMEOUT,
            "Navigation timeout must remain TIMEOUT, not be reclassified as TRANSPORT");
        result.DetailCode.Should().Be("browser.navigationTimeout");
    }

    [Fact]
    public void AC1_OperationCancelled_IsClassifiedAsCancelled()
    {
        var sut = new FailureClassifier(NullLogger<FailureClassifier>.Instance);
        var result = sut.Classify(new OperationCanceledException("cancelled"), stateKnown: false);

        result.Category.Should().Be(ErrorCategory.CANCELLED);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // AC#2 — Connection drop during form submit → UNKNOWN (not auto-retry)
    // BRW-AT-12, BRW-AT-23
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AC2_TransportError_WithStateUnknown_CallerReceivesTransportCategory()
    {
        // Arrange — the caller (run orchestrator) checks stateKnown=false + TRANSPORT
        // to decide the run state should be UNKNOWN.
        var sut = new FailureClassifier(NullLogger<FailureClassifier>.Instance);
        var ex  = new PlaywrightException(
            "net::ERR_CONNECTION_RESET at https://example.com/submit");

        // Act — stateKnown=false because submit outcome is ambiguous
        var result = sut.Classify(ex, stateKnown: false);

        // Assert — the classifier returns TRANSPORT; the orchestrator maps this to UNKNOWN
        result.Category.Should().Be(ErrorCategory.TRANSPORT,
            "AC#2: connection drop during submit → TRANSPORT; " +
            "run orchestrator converts this to UNKNOWN run state when stateKnown=false");
        result.DetailCode.Should().Be("browser.networkOffline",
            "AC#2: connection-drop detail code must be browser.networkOffline");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // AC#3 — UNKNOWN_OUTCOME from service.invoke → terminate as UNKNOWN, no actionCode
    // BRW-AT-11, BRW-AT-24, BRW-AT-25
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC3_ActionExecutor_ServiceInvoke_UnknownOutcome_ReturnsUnknownOutcomeResult()
    {
        // Arrange — set up a service invoker that returns UNKNOWN_OUTCOME
        var invokerMock = new Mock<IServiceInvoker>();
        invokerMock
            .Setup(x => x.InvokeAsync(
                It.IsAny<ServiceInvokeRef>(),
                It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInvokeResult
            {
                Outcome    = ServiceOutcome.UNKNOWN_OUTCOME,
                ActionCode = null
            });

        var selectorMock = new Mock<ISelectorStrategy>();
        var executor = new ActionExecutor(
            selectorStrategy:    selectorMock.Object,
            failureClassifier:   new FailureClassifier(NullLogger<FailureClassifier>.Instance),
            logger:              NullLogger<ActionExecutor>.Instance,
            serviceInvoker:      invokerMock.Object);

        var step = new BrowserStep
        {
            StepId     = "step-svc-001",
            Action     = "service.invoke",
            ServiceRef = new ServiceInvokeRef
            {
                ServiceId    = "PaymentService",
                InputMapping = new Dictionary<string, object?> { ["amount"] = "100.00" }
            }
        };

        // Act
        var result = await executor.ExecuteAsync(Mock.Of<IPage>(), step, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse(
            "AC#3: UNKNOWN_OUTCOME must be a failure — the run terminates as UNKNOWN");

        result.ServiceOutcome.Should().Be(ServiceOutcome.UNKNOWN_OUTCOME,
            "AC#3: the service outcome must be propagated to the caller");

        result.ServiceActionCode.Should().BeNull(
            "AC#3: no actionCode branch is permitted for UNKNOWN_OUTCOME");

        result.DetailCode.Should().Be("browser.serviceUnknownOutcome",
            "AC#3: a specific detail code must identify the UNKNOWN_OUTCOME scenario");
    }

    [Fact]
    public async Task AC3_ActionExecutor_ServiceInvoke_UnknownOutcome_EvidenceContainsServiceId()
    {
        // Arrange
        var invokerMock = new Mock<IServiceInvoker>();
        invokerMock
            .Setup(x => x.InvokeAsync(
                It.IsAny<ServiceInvokeRef>(),
                It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInvokeResult { Outcome = ServiceOutcome.UNKNOWN_OUTCOME });

        var executor = new ActionExecutor(
            selectorStrategy:  Mock.Of<ISelectorStrategy>(),
            failureClassifier: new FailureClassifier(NullLogger<FailureClassifier>.Instance),
            logger:            NullLogger<ActionExecutor>.Instance,
            serviceInvoker:    invokerMock.Object);

        var step = new BrowserStep
        {
            StepId     = "step-svc-002",
            Action     = "service.invoke",
            ServiceRef = new ServiceInvokeRef { ServiceId = "ReconciliationService" }
        };

        // Act
        var result = await executor.ExecuteAsync(Mock.Of<IPage>(), step, CancellationToken.None);

        // Assert — evidence must record both the serviceId and the outcome for diagnostics
        result.Evidence.Should().ContainKey("serviceId")
            .WhoseValue.Should().Be("ReconciliationService");
        result.Evidence.Should().ContainKey("serviceOutcome")
            .WhoseValue.Should().Be("UNKNOWN_OUTCOME");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // AC#4 — Definitive outcomes → actionCode branch may proceed
    // BRW-AT-11, BRW-AT-20, BRW-AT-24, BRW-AT-25
    // ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ServiceOutcome.SUCCESS,            true,  "SHOW_CONFIRMATION")]
    [InlineData(ServiceOutcome.BUSINESS_REJECTION, true,  "SHOW_REJECTION_STATUS")]
    [InlineData(ServiceOutcome.TECHNICAL_FAILURE,  false, null)]
    [InlineData(ServiceOutcome.TRANSIENT_FAILURE,  false, null)]
    public async Task AC4_ActionExecutor_ServiceInvoke_DefinitiveOutcome_ForwardsActionCode(
        ServiceOutcome outcome,
        bool expectedSucceeded,
        string? actionCode)
    {
        // Arrange — the service returns a definitive outcome with an actionCode
        var invokerMock = new Mock<IServiceInvoker>();
        invokerMock
            .Setup(x => x.InvokeAsync(
                It.IsAny<ServiceInvokeRef>(),
                It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInvokeResult
            {
                Outcome    = outcome,
                ActionCode = actionCode
            });

        var executor = new ActionExecutor(
            selectorStrategy:  Mock.Of<ISelectorStrategy>(),
            failureClassifier: new FailureClassifier(NullLogger<FailureClassifier>.Instance),
            logger:            NullLogger<ActionExecutor>.Instance,
            serviceInvoker:    invokerMock.Object);

        var step = new BrowserStep
        {
            StepId     = "step-svc-003",
            Action     = "service.invoke",
            ServiceRef = new ServiceInvokeRef { ServiceId = "PaymentService" }
        };

        // Act
        var result = await executor.ExecuteAsync(Mock.Of<IPage>(), step, CancellationToken.None);

        // Assert
        result.ServiceOutcome.Should().Be(outcome,
            "AC#4: definitive outcome must be propagated to the caller");
        result.Succeeded.Should().Be(expectedSucceeded,
            $"AC#4: {outcome} must produce Succeeded={expectedSucceeded}");
        result.ServiceActionCode.Should().Be(actionCode,
            "AC#4: the service-supplied actionCode must be forwarded to the run orchestrator");
    }

    [Fact]
    public async Task AC4_ActionExecutor_ServiceInvoke_BusinessRejection_EvidenceContainsOutcome()
    {
        // Arrange — BUSINESS_REJECTION is a definitive outcome (BRW-US-018 AC#2)
        var invokerMock = new Mock<IServiceInvoker>();
        invokerMock
            .Setup(x => x.InvokeAsync(
                It.IsAny<ServiceInvokeRef>(),
                It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInvokeResult
            {
                Outcome    = ServiceOutcome.BUSINESS_REJECTION,
                ActionCode = "SHOW_REJECTION_STATUS",
                Output     = new Dictionary<string, object?> { ["rejectionReason"] = "INSUFFICIENT_FUNDS" }
            });

        var executor = new ActionExecutor(
            selectorStrategy:  Mock.Of<ISelectorStrategy>(),
            failureClassifier: new FailureClassifier(NullLogger<FailureClassifier>.Instance),
            logger:            NullLogger<ActionExecutor>.Instance,
            serviceInvoker:    invokerMock.Object);

        var step = new BrowserStep
        {
            StepId     = "step-svc-004",
            Action     = "service.invoke",
            ServiceRef = new ServiceInvokeRef { ServiceId = "PaymentService" }
        };

        // Act
        var result = await executor.ExecuteAsync(Mock.Of<IPage>(), step, CancellationToken.None);

        // Assert — BUSINESS_REJECTION succeeds at the invoke level (AC#4)
        result.Succeeded.Should().BeTrue(
            "AC#4: BUSINESS_REJECTION is a definitive service outcome; " +
            "Succeeded=true because the invoke completed; run state is determined by the orchestrator");
        result.ServiceOutcome.Should().Be(ServiceOutcome.BUSINESS_REJECTION);
        result.ServiceActionCode.Should().Be("SHOW_REJECTION_STATUS");
        result.Evidence.Should().ContainKey("rejectionReason")
            .WhoseValue.Should().Be("INSUFFICIENT_FUNDS",
                "service output must be merged into evidence");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // ServiceInvoker — outcome mapping and UNKNOWN_OUTCOME guard (BRW-US-015 AC#3)
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ServiceInvoker_UnregisteredService_ReturnsTechnicalFailure()
    {
        // Arrange — options has no endpoint entry for the requested serviceId
        var httpClient   = new HttpClient();
        var invokerOpts  = Microsoft.Extensions.Options.Options.Create(new ServiceInvokerOptions());
        var invoker      = new ServiceInvoker(httpClient, invokerOpts, NullLogger<ServiceInvoker>.Instance);

        // Act
        Func<Task> act = async () =>
        {
            var result = await invoker.InvokeAsync(
                new ServiceInvokeRef { ServiceId = "NonExistentService" },
                [],
                CancellationToken.None);

            // Assert inside the lambda so we can check outcome directly
            result.Outcome.Should().Be(ServiceOutcome.TECHNICAL_FAILURE,
                "AC#3: an unregistered service must return TECHNICAL_FAILURE, not throw");
            result.ActionCode.Should().BeNull();
        };

        act.Should().NotThrowAsync();
    }

    [Fact]
    public void ServiceRegistry_RegisteredEndpoint_FoundByServiceId()
    {
        // Arrange
        var registry = new ServiceRegistry(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PaymentService"] = "https://payments.internal/invoke"
            });

        // Act
        bool found = registry.TryGetEndpoint("PaymentService", out var endpoint);

        // Assert
        found.Should().BeTrue();
        endpoint.Should().NotBeNull();
        endpoint!.Host.Should().Be("payments.internal");
    }

    [Fact]
    public void ServiceRegistry_UnknownServiceId_NotFound()
    {
        var registry = new ServiceRegistry(new Dictionary<string, string>());

        bool found = registry.TryGetEndpoint("UnknownService", out var endpoint);

        found.Should().BeFalse();
        endpoint.Should().BeNull();
    }

    [Fact]
    public void ServiceRegistry_InvalidUrl_ThrowsOnConstruction()
    {
        // Arrange / Act
        var act = () => new ServiceRegistry(
            new Dictionary<string, string> { ["Bad"] = "not-a-valid-url" });

        // Assert — invalid URLs must be rejected at startup, not silently ignored
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Bad*");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // ActionExecutor — service.invoke precondition guards
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ServiceInvoke_WithoutServiceInvoker_ReturnsInternalError()
    {
        // Arrange — ActionExecutor constructed WITHOUT an IServiceInvoker
        var executor = new ActionExecutor(
            selectorStrategy:  Mock.Of<ISelectorStrategy>(),
            failureClassifier: new FailureClassifier(NullLogger<FailureClassifier>.Instance),
            logger:            NullLogger<ActionExecutor>.Instance);

        var step = new BrowserStep
        {
            StepId     = "step-no-invoker",
            Action     = "service.invoke",
            ServiceRef = new ServiceInvokeRef { ServiceId = "SomeService" }
        };

        // Act
        var result = await executor.ExecuteAsync(Mock.Of<IPage>(), step, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.INTERNAL);
        result.DetailCode.Should().Be("browser.serviceInvokerNotConfigured");
    }

    [Fact]
    public async Task ServiceInvoke_WithoutServiceRef_ReturnsValidationError()
    {
        // Arrange — step has no ServiceRef
        var executor = new ActionExecutor(
            selectorStrategy:  Mock.Of<ISelectorStrategy>(),
            failureClassifier: new FailureClassifier(NullLogger<FailureClassifier>.Instance),
            logger:            NullLogger<ActionExecutor>.Instance,
            serviceInvoker:    Mock.Of<IServiceInvoker>());

        var step = new BrowserStep
        {
            StepId     = "step-no-ref",
            Action     = "service.invoke",
            ServiceRef = null  // Missing!
        };

        // Act
        var result = await executor.ExecuteAsync(Mock.Of<IPage>(), step, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.VALIDATION);
        result.DetailCode.Should().Be("browser.missingServiceRef");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // AC#5 — Journal persistence prevents duplicate submission (BRW-AT-12)
    // Tested through ReconciliationService — verifies HasCommittedSideEffect for
    // service.invoke entries in the journal.
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC5_ReconciliationService_ServiceInvokeInJournal_HasCommittedSideEffect()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        var journal    = new Worker.RunJournal(tempDir.Path, NullLogger<Worker.RunJournal>.Instance);
        var reconciler = new Worker.ReconciliationService(journal, NullLogger<Worker.ReconciliationService>.Instance);
        const string runId = "run-015-ac5-001";

        await journal.OpenAsync(runId, CancellationToken.None);
        // Record a service.invoke step with a committed outcome
        await journal.AppendStepAsync(runId, "step-1", "service.invoke",
            new { outcome = "SUCCESS" }, CancellationToken.None);

        // Act — reconcile (simulates worker restart)
        var result = await reconciler.ReconcileAsync(runId, CancellationToken.None);

        // Assert
        result.HasCommittedSideEffect.Should().BeTrue(
            "AC#5: a service.invoke entry in the journal must flag HasCommittedSideEffect=true " +
            "so the worker does not replay the action after an Orchestrator restart");
    }

    [Fact]
    public async Task AC5_ReconciliationService_NoJournal_NoCommittedSideEffect()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        var journal    = new Worker.RunJournal(tempDir.Path, NullLogger<Worker.RunJournal>.Instance);
        var reconciler = new Worker.ReconciliationService(journal, NullLogger<Worker.ReconciliationService>.Instance);

        // Act — reconcile a run that never started
        var result = await reconciler.ReconcileAsync("nonexistent-run-id", CancellationToken.None);

        // Assert
        result.Verdict.Should().Be(Worker.ReconciliationVerdict.NoJournalFound);
        result.HasCommittedSideEffect.Should().BeFalse();
    }
}

/// <summary>Disposable helper that creates and cleans up a temp directory for test file I/O.</summary>
file sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        $"tracto-brw-us015-{Guid.NewGuid():N}");

    public TempDirectory() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}
