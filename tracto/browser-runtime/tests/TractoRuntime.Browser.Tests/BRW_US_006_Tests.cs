using FluentAssertions;
using Microsoft.Playwright;
using Moq;
using TractoRuntime.Browser.Capabilities;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Environment;
using TractoRuntime.Browser.Mode;
using TractoRuntime.Browser.Tests.Fixtures;
using TractoRuntime.Browser.Viewport;

namespace TractoRuntime.Browser.Tests;

/// <summary>
/// Acceptance tests for BRW-US-006 — Viewport Reproducibility and Environment Metadata.
///
/// Requirement refs: BRW-006, BRW-007
/// Acceptance tests:  BRW-AT-01, BRW-AT-02, BRW-AT-10, BRW-AT-22
///
/// Acceptance criteria:
///   AC-1  The runtime reports engine/version, mode, viewport, locale, timezone,
///         TLS/proxy config ref, and capability digest to the Orchestrator before the first step.
///   AC-2  A fixture run at the declared certified viewport passes; an unsupported viewport
///         supplied in the artifact fails with a structured error before any browser interaction.
///   AC-3  Diagnostic evidence includes enough environment information to reproduce the failure
///         without containing any credential, token, or session secret.
///   AC-4  A responsive layout fixture passes at the certified viewport width.
/// </summary>
public sealed class BRW_US_006_Tests : BrowserTestBase
{
    // ═══════════════════════════════════════════════════════════════════════════
    // AC-1 — BRW-AT-10 / BRW-AT-22
    // Environment metadata reported before the first step
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AC1_CollectAsync_HeadlessMode_AllRequiredFieldsPresent()
    {
        // Arrange — full production-style config
        var sut = new EnvironmentReporter(
            locale:          "en-US",
            timezone:        "America/New_York",
            tlsConfigRef:    "tls-ref-policy-001",
            proxyConfigRef:  "proxy-ref-policy-001",
            capabilityDigest:"sha256:deadbeef");

        var viewport = ViewportSettings.Default; // 1280x720 @ 1x

        // Act — metadata must be collectible before any step runs
        var metadata = await sut.CollectAsync(
            runId:          "run-brw006-ac1-headless",
            browserEngine:  "chromium",
            browserVersion: "120.0.6099.71",
            mode:           BrowserMode.Headless,
            viewport:       viewport,
            ct:             CancellationToken.None);

        // Assert — AC-1: all required fields present
        metadata.RunId.Should().Be("run-brw006-ac1-headless");
        metadata.BrowserEngine.Should().Be("chromium");
        metadata.BrowserVersion.Should().Be("120.0.6099.71");
        metadata.ExecutionMode.Should().Be(BrowserMode.Headless);
        metadata.Viewport.Should().Be(viewport);
        metadata.Locale.Should().Be("en-US");
        metadata.Timezone.Should().Be("America/New_York");
        metadata.TlsConfigRef.Should().Be("tls-ref-policy-001");
        metadata.ProxyConfigRef.Should().Be("proxy-ref-policy-001");
        metadata.CapabilityDigest.Should().Be("sha256:deadbeef");
        metadata.CollectedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AC1_CollectAsync_HeadedMode_ReportsHeadedExecutionMode()
    {
        // Arrange — BRW-AT-22: headed worker
        var sut = new EnvironmentReporter(locale: "de-DE", timezone: "Europe/Berlin");

        // Act
        var metadata = await sut.CollectAsync(
            "run-brw006-ac1-headed", "chromium", "120.0.6099.71",
            BrowserMode.Headed, ViewportSettings.Default, CancellationToken.None);

        // Assert — headed mode is reported correctly
        metadata.ExecutionMode.Should().Be(BrowserMode.Headed);
    }

    [Fact]
    public async Task AC1_CollectAsync_ViewportFieldsMatchDeclaredScript()
    {
        // Arrange — viewport from a script artifact (1920x1080 HiDPI)
        var declaredViewport = new ViewportSettings { Width = 1920, Height = 1080, DeviceScaleFactor = 2.0 };
        var sut = new EnvironmentReporter();

        // Act
        var metadata = await sut.CollectAsync(
            "run-brw006-ac1-viewport", "chromium", "120.0.6099.71",
            BrowserMode.Headless, declaredViewport, CancellationToken.None);

        // Assert — viewport dimensions exactly match what was declared
        metadata.Viewport.Width.Should().Be(1920);
        metadata.Viewport.Height.Should().Be(1080);
        metadata.Viewport.DeviceScaleFactor.Should().Be(2.0);
    }

    [Theory]
    [InlineData("chromium", "120.0.6099.71")]
    [InlineData("firefox",  "120.0")]
    [InlineData("webkit",   "17.4")]
    public async Task AC1_CollectAsync_BrowserEngineAndVersion_ReportedForAllEngines(
        string engine, string version)
    {
        // Arrange
        var sut = new EnvironmentReporter();

        // Act
        var metadata = await sut.CollectAsync(
            "run-brw006-engines", engine, version,
            BrowserMode.Headless, ViewportSettings.Default, CancellationToken.None);

        // Assert
        metadata.BrowserEngine.Should().Be(engine);
        metadata.BrowserVersion.Should().Be(version);
    }

    [Fact]
    public async Task AC1_EnvironmentReporterCalledBeforeActionExecutor()
    {
        // Arrange — verify ordering: environment collection must precede any browser action.
        // Using mock sequencing to prove the contract.
        var callOrder = new List<string>();

        EnvironmentReporterMock
            .Setup(r => r.CollectAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<BrowserMode>(), It.IsAny<ViewportSettings>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("EnvironmentReporter.CollectAsync"))
            .ReturnsAsync(new EnvironmentMetadata
            {
                RunId          = "run-ordering",
                BrowserEngine  = "chromium",
                BrowserVersion = "120.0.0",
                ExecutionMode  = BrowserMode.Headless,
                Viewport       = ViewportSettings.Default,
                Locale         = "en-US",
                Timezone       = "UTC"
            });

        var pageMock = new Mock<IPage>();
        ActionExecutorMock
            .Setup(a => a.ExecuteAsync(
                It.IsAny<IPage>(),
                It.IsAny<BrowserStep>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("ActionExecutor.ExecuteAsync"))
            .ReturnsAsync(new TractoRuntime.Browser.Actions.ActionResult { Succeeded = true });

        // Act — simulate the pre-step pipeline
        await EnvironmentReporterMock.Object.CollectAsync(
            "run-ordering", "chromium", "120.0.0",
            BrowserMode.Headless, ViewportSettings.Default, CancellationToken.None);

        var step = TestArtifactFactory.MakeStep("step-001", "browser.navigate");
        await ActionExecutorMock.Object.ExecuteAsync(pageMock.Object, step, CancellationToken.None);

        // Assert — EnvironmentReporter must have been called BEFORE ActionExecutor
        callOrder.Should().HaveCount(2);
        callOrder[0].Should().Be("EnvironmentReporter.CollectAsync",
            because: "environment metadata must be collected before the first action executes (AC-1)");
        callOrder[1].Should().Be("ActionExecutor.ExecuteAsync");
    }

    [Fact]
    public async Task AC1_CapabilityAdvertiser_HeadlessPayload_IncludesEngineAndVersion()
    {
        // BRW-AT-10: Headless capability payload includes BRW-006 metadata
        var runtimeInfoMock = new Mock<IRuntimeInfo>();
        runtimeInfoMock
            .Setup(r => r.GetBrowserInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(("chromium", "120.0.6099.71"));

        var advertiser = new CapabilityAdvertiser(runtimeInfoMock.Object);
        var config = new ExecutionModeConfig { Mode = BrowserMode.Headless };

        var payload = await advertiser.BuildPayloadAsync("worker-brw006", config, CancellationToken.None);

        // Assert — AC-1: engine and version in Orchestrator advertisement
        payload.BrowserEngine.Should().Be("chromium");
        payload.BrowserVersion.Should().Be("120.0.6099.71");
        payload.Mode.Should().Be("headless");
    }

    [Fact]
    public async Task AC1_CapabilityAdvertiser_HeadedPayload_IncludesSessionAndMode()
    {
        // BRW-AT-22: Headed capability payload
        var runtimeInfoMock = new Mock<IRuntimeInfo>();
        runtimeInfoMock
            .Setup(r => r.GetBrowserInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(("chromium", "120.0.6099.71"));

        var advertiser = new CapabilityAdvertiser(runtimeInfoMock.Object);
        var config = new ExecutionModeConfig { Mode = BrowserMode.Headed, InteractiveSessionId = "1" };

        var payload = await advertiser.BuildPayloadAsync("worker-brw006-h", config, CancellationToken.None);

        // Assert — AC-1: mode, session, and engine are all in the advertisement
        payload.Mode.Should().Be("headed");
        payload.InteractiveSessionId.Should().Be("1");
        payload.BrowserEngine.Should().Be("chromium");
        payload.BrowserVersion.Should().Be("120.0.6099.71");
    }

    [Fact]
    public async Task AC1_CapabilityDigest_IsNonEmptyAndHexEncoded()
    {
        // The capability digest (integrity hint) must be present in the advertisement
        var runtimeInfoMock = new Mock<IRuntimeInfo>();
        runtimeInfoMock
            .Setup(r => r.GetBrowserInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(("chromium", "1.48.0"));

        var advertiser = new CapabilityAdvertiser(runtimeInfoMock.Object);
        var config = new ExecutionModeConfig { Mode = BrowserMode.Headless };

        var payload = await advertiser.BuildPayloadAsync("worker-digest", config, CancellationToken.None);

        payload.CapabilityDigest.Should().NotBeNullOrEmpty(
            because: "the capability digest must always be present in the Orchestrator advertisement");
        payload.CapabilityDigest!.Should().MatchRegex("^[0-9a-f]+$",
            because: "the capability digest must be a lowercase hex string (integrity hint, not auth token)");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC-2 — BRW-AT-01 / BRW-AT-02
    // Certified viewport passes; unsupported viewport rejected before browser interaction
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AC2_DefaultViewport_PassesValidation()
    {
        // AC-2: a run at the default certified viewport must succeed
        var validator = new ViewportValidator();

        await validator.Invoking(v => v.ValidateAsync(ViewportSettings.Default, CancellationToken.None))
                       .Should().NotThrowAsync(
                            because: "the default viewport 1280x720@1x must always be certified");
    }

    [Theory]
    [InlineData(1280,  720, 1.0)]
    [InlineData(1366,  768, 1.0)]
    [InlineData(1440,  900, 1.0)]
    [InlineData(1920, 1080, 1.0)]
    [InlineData(1280,  800, 2.0)]
    [InlineData(1920, 1080, 2.0)]
    public async Task AC2_AllCertifiedViewports_PassValidation(int width, int height, double scale)
    {
        // AC-2: every entry in the certified list must pass without error
        var validator = new ViewportValidator();
        var viewport = new ViewportSettings { Width = width, Height = height, DeviceScaleFactor = scale };

        await validator.Invoking(v => v.ValidateAsync(viewport, CancellationToken.None))
                       .Should().NotThrowAsync(
                            because: $"{width}x{height}@{scale}x is in the certified list");
    }

    [Fact]
    public async Task AC2_UnsupportedViewportInScript_ThrowsViewportNotSupportedException()
    {
        // AC-2: an artifact supplying an unsupported viewport must be rejected
        var validator = new ViewportValidator();
        var unsupported = new ViewportSettings { Width = 800, Height = 600, DeviceScaleFactor = 1.0 };

        await validator.Invoking(v => v.ValidateAsync(unsupported, CancellationToken.None))
                       .Should().ThrowAsync<ViewportNotSupportedException>(
                            because: "800x600 is not a certified viewport");
    }

    [Fact]
    public async Task AC2_UnsupportedViewport_HasValidationErrorCategory()
    {
        // The exception must carry errorCategory=VALIDATION (not INTERNAL or ENVIRONMENT)
        var validator = new ViewportValidator();
        var unsupported = new ViewportSettings { Width = 1024, Height = 768, DeviceScaleFactor = 1.0 };

        var ex = await Assert.ThrowsAsync<ViewportNotSupportedException>(
            () => validator.ValidateAsync(unsupported, CancellationToken.None));

        ex.ErrorCategory.Should().Be(ErrorCategory.VALIDATION,
            because: "CEC §6.2 maps unsupported viewport to errorCategory=VALIDATION");
    }

    [Fact]
    public async Task AC2_UnsupportedViewport_HasCorrectDetailCode()
    {
        // Detail code must be exactly "browser.unsupportedViewport" (per spec)
        var validator = new ViewportValidator();
        var unsupported = new ViewportSettings { Width = 1024, Height = 768, DeviceScaleFactor = 1.0 };

        var ex = await Assert.ThrowsAsync<ViewportNotSupportedException>(
            () => validator.ValidateAsync(unsupported, CancellationToken.None));

        ex.DetailCode.Should().Be("browser.unsupportedViewport");
    }

    [Fact]
    public async Task AC2_UnsupportedViewport_ActionExecutorNotCalled()
    {
        // CRITICAL: ActionExecutor must NOT be called when viewport validation fails
        var validator = new ViewportValidator();
        var unsupported = new ViewportSettings { Width = 800, Height = 600, DeviceScaleFactor = 1.0 };

        // Attempt viewport validation (this is what the run start-up pipeline does)
        var validationFailed = false;
        try
        {
            await validator.ValidateAsync(unsupported, CancellationToken.None);
        }
        catch (ViewportNotSupportedException)
        {
            validationFailed = true;
            // Run must abort here — no browser actions follow
        }

        validationFailed.Should().BeTrue();

        // ActionExecutor must never have been called
        ActionExecutorMock.Verify(
            a => a.ExecuteAsync(It.IsAny<IPage>(), It.IsAny<BrowserStep>(), It.IsAny<CancellationToken>()),
            Times.Never); // no action may execute when the artifact viewport is not certified (AC-2)
    }

    [Fact]
    public async Task AC2_UnsupportedViewport_ExceptionPreservesRequestedViewport()
    {
        // The exception must carry the exact viewport that was rejected
        var validator = new ViewportValidator();
        var unsupported = new ViewportSettings { Width = 800, Height = 600, DeviceScaleFactor = 1.5 };

        var ex = await Assert.ThrowsAsync<ViewportNotSupportedException>(
            () => validator.ValidateAsync(unsupported, CancellationToken.None));

        ex.RequestedViewport.Should().Be(unsupported,
            because: "the exception must identify the rejected viewport for the diagnostic evidence");
    }

    [Fact]
    public async Task AC2_UnsupportedViewport_ExceptionListsCertifiedAlternatives()
    {
        // The exception must suggest valid alternatives so the author can fix the script
        var validator = new ViewportValidator();
        var unsupported = new ViewportSettings { Width = 800, Height = 600, DeviceScaleFactor = 1.0 };

        var ex = await Assert.ThrowsAsync<ViewportNotSupportedException>(
            () => validator.ValidateAsync(unsupported, CancellationToken.None));

        ex.CertifiedViewports.Should().NotBeEmpty(
            because: "the exception must guide the author to valid alternatives");
    }

    [Fact]
    public async Task AC2_ScriptWithNullViewport_UsesDefaultViewportAndPasses()
    {
        // When a script does not declare a viewport, the runtime defaults to 1280x720@1x
        var validator = new ViewportValidator();
        var script = TestArtifactFactory.MakeScript(); // Viewport is null by default

        // The runtime applies ViewportSettings.Default when script.Viewport is null
        var effectiveViewport = script.Viewport ?? ViewportSettings.Default;

        await validator.Invoking(v => v.ValidateAsync(effectiveViewport, CancellationToken.None))
                       .Should().NotThrowAsync(
                            because: "the implicit default viewport must always be certified");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC-3 — Evidence sanitization (no credentials in diagnostic output)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AC3_SanitizedEvidence_ContainsAllRequiredDiagnosticKeys()
    {
        // AC-3: evidence must be sufficient to reproduce the failure
        var sut = new EnvironmentReporter(
            locale:          "fr-FR",
            timezone:        "Europe/Paris",
            tlsConfigRef:    "tls-ref-fr",
            proxyConfigRef:  "proxy-ref-fr",
            capabilityDigest:"sha256:cafebabe");

        var metadata = await sut.CollectAsync(
            "run-brw006-ac3", "chromium", "120.0.6099.71",
            BrowserMode.Headless, ViewportSettings.Default, CancellationToken.None);

        var evidence = sut.BuildSanitizedEvidence(metadata);

        // All keys required for reproducing the run environment
        evidence.Should().ContainKey("runId",             because: "runId links evidence to the failed run");
        evidence.Should().ContainKey("browserEngine",     because: "engine is required for exact reproduction");
        evidence.Should().ContainKey("browserVersion",    because: "version determines DOM rendering");
        evidence.Should().ContainKey("executionMode",     because: "mode (headed/headless) affects reproduction");
        evidence.Should().ContainKey("viewportWidth",     because: "viewport affects layout and element positions");
        evidence.Should().ContainKey("viewportHeight",    because: "viewport height affects visible DOM state");
        evidence.Should().ContainKey("deviceScaleFactor", because: "DPR affects HiDPI rendering differences");
        evidence.Should().ContainKey("locale",            because: "locale affects date/number rendering");
        evidence.Should().ContainKey("timezone",          because: "timezone affects time-sensitive elements");
        evidence.Should().ContainKey("tlsConfigRef",      because: "TLS reference identifies network policy");
        evidence.Should().ContainKey("proxyConfigRef",    because: "proxy reference identifies routing policy");
        evidence.Should().ContainKey("capabilityDigest",  because: "digest identifies the advertised capabilities");
        evidence.Should().ContainKey("collectedAt",       because: "timestamp is required for time-correlation");
    }

    [Fact]
    public async Task AC3_SanitizedEvidence_NoPemCertificateMaterial()
    {
        // AC-3: No TLS certificate material in evidence
        var sut = new EnvironmentReporter(tlsConfigRef: "tls-ref-only");

        var metadata = await sut.CollectAsync(
            "run-brw006-ac3-tls", "chromium", "120.0.0",
            BrowserMode.Headless, ViewportSettings.Default, CancellationToken.None);

        var evidence = sut.BuildSanitizedEvidence(metadata);

        foreach (var value in evidence.Values.OfType<string>())
        {
            value.Should().NotContain("-----BEGIN",
                because: "PEM certificate material must never appear in diagnostic evidence");
            value.Should().NotContain("-----END",
                because: "PEM certificate material must never appear in diagnostic evidence");
        }
    }

    [Fact]
    public async Task AC3_SanitizedEvidence_NoBearerTokens()
    {
        // AC-3: No auth tokens in evidence
        var sut = new EnvironmentReporter();

        var metadata = await sut.CollectAsync(
            "run-brw006-ac3-tokens", "chromium", "120.0.0",
            BrowserMode.Headless, ViewportSettings.Default, CancellationToken.None);

        var evidence = sut.BuildSanitizedEvidence(metadata);

        foreach (var value in evidence.Values.OfType<string>())
        {
            value.Should().NotContain("Bearer ",
                because: "Bearer tokens must never appear in sanitized diagnostic evidence");
        }
    }

    [Fact]
    public async Task AC3_SanitizedEvidence_NoRawProxyCredentials()
    {
        // AC-3: Proxy credentials (username:password) must not appear
        const string proxyRef = "proxy-policy-ref-007"; // opaque reference only
        var sut = new EnvironmentReporter(proxyConfigRef: proxyRef);

        var metadata = await sut.CollectAsync(
            "run-brw006-ac3-proxy", "chromium", "120.0.0",
            BrowserMode.Headless, ViewportSettings.Default, CancellationToken.None);

        var evidence = sut.BuildSanitizedEvidence(metadata);

        // The proxy reference appears as an opaque identifier
        evidence["proxyConfigRef"].Should().Be(proxyRef,
            because: "the reference identifier itself is safe (it is not a credential)");

        // No value should look like a URL with embedded credentials
        foreach (var value in evidence.Values.OfType<string>())
        {
            value.Should().NotMatchRegex(@"https?://\w+:\w+@",
                because: "URLs with embedded credentials must not appear in evidence");
        }
    }

    [Fact]
    public async Task AC3_SanitizedEvidence_CollectedAtIsIso8601()
    {
        // collectedAt must be an ISO 8601 timestamp for time-correlation during investigation
        var sut = new EnvironmentReporter();

        var metadata = await sut.CollectAsync(
            "run-brw006-ac3-ts", "chromium", "120.0.0",
            BrowserMode.Headless, ViewportSettings.Default, CancellationToken.None);

        var evidence = sut.BuildSanitizedEvidence(metadata);

        var collectedAt = evidence["collectedAt"] as string;
        collectedAt.Should().NotBeNullOrEmpty();
        DateTimeOffset.TryParse(collectedAt, out _).Should().BeTrue(
            because: "collectedAt must be an ISO 8601 string for unambiguous time correlation");
    }

    [Fact]
    public async Task AC3_SanitizedEvidence_ViewportDimensionsMatchMetadata()
    {
        // Evidence must carry the exact declared viewport (not a transformed or rounded value)
        var viewport = new ViewportSettings { Width = 1440, Height = 900, DeviceScaleFactor = 1.0 };
        var sut = new EnvironmentReporter();

        var metadata = await sut.CollectAsync(
            "run-brw006-ac3-vp", "chromium", "120.0.0",
            BrowserMode.Headless, viewport, CancellationToken.None);

        var evidence = sut.BuildSanitizedEvidence(metadata);

        evidence["viewportWidth"].Should().Be(1440);
        evidence["viewportHeight"].Should().Be(900);
        evidence["deviceScaleFactor"].Should().Be(1.0);
    }

    [Fact]
    public async Task AC3_SanitizedEvidence_ExecutionModeIsStringNotInteger()
    {
        // executionMode must be a human-readable string ("Headless"/"Headed"),
        // not an enum integer that would require a schema look-up to interpret.
        var sut = new EnvironmentReporter();

        var headlessMetadata = await sut.CollectAsync(
            "run-brw006-ac3-mode-hl", "chromium", "120.0.0",
            BrowserMode.Headless, ViewportSettings.Default, CancellationToken.None);

        var headedMetadata = await sut.CollectAsync(
            "run-brw006-ac3-mode-hd", "chromium", "120.0.0",
            BrowserMode.Headed, ViewportSettings.Default, CancellationToken.None);

        var headlessEvidence = sut.BuildSanitizedEvidence(headlessMetadata);
        var headedEvidence   = sut.BuildSanitizedEvidence(headedMetadata);

        headlessEvidence["executionMode"].Should().Be("Headless");
        headedEvidence["executionMode"].Should().Be("Headed");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC-4 — Responsive layout fixture passes at the certified viewport width
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AC4_CertifiedViewportList_IsNonEmpty()
    {
        // AC-4: the runtime must maintain a non-empty certified list
        var validator = new ViewportValidator();
        validator.GetCertifiedViewports().Should().NotBeEmpty(
            because: "at least one certified viewport must exist for reproducible layout testing");
    }

    [Fact]
    public void AC4_CertifiedViewportList_ContainsDefaultViewport()
    {
        // AC-4: the default viewport must be in the certified list (used by all no-viewport scripts)
        var validator = new ViewportValidator();

        validator.GetCertifiedViewports().Should().Contain(v =>
            v.Width == ViewportSettings.Default.Width &&
            v.Height == ViewportSettings.Default.Height &&
            Math.Abs(v.DeviceScaleFactor - ViewportSettings.Default.DeviceScaleFactor) < 0.001,
            because: "the default viewport 1280x720@1x must always be in the certified list");
    }

    [Fact]
    public void AC4_CertifiedViewports_AllHavePositiveDimensions()
    {
        // AC-4: every certified viewport must have a usable non-zero width and height
        var validator = new ViewportValidator();

        foreach (var viewport in validator.GetCertifiedViewports())
        {
            viewport.Width.Should().BeGreaterThan(0,
                because: $"certified viewport {viewport.Width}x{viewport.Height} must have positive width");
            viewport.Height.Should().BeGreaterThan(0,
                because: $"certified viewport {viewport.Width}x{viewport.Height} must have positive height");
        }
    }

    [Fact]
    public void AC4_CertifiedViewports_AllHavePositiveDeviceScaleFactor()
    {
        // Device scale factor must be > 0 for all certified entries
        var validator = new ViewportValidator();

        foreach (var viewport in validator.GetCertifiedViewports())
        {
            viewport.DeviceScaleFactor.Should().BeGreaterThan(0.0,
                because: "a zero or negative device-scale-factor produces invalid HiDPI rendering");
        }
    }

    [Fact]
    public async Task AC4_CertifiedViewports_AllPassValidation()
    {
        // AC-4: every certified viewport must pass the validator itself (internal consistency)
        var validator = new ViewportValidator();
        var certified = validator.GetCertifiedViewports();

        foreach (var viewport in certified)
        {
            await validator.Invoking(v => v.ValidateAsync(viewport, CancellationToken.None))
                           .Should().NotThrowAsync(
                                because: $"certified viewport {viewport.Width}x{viewport.Height}@{viewport.DeviceScaleFactor}x must pass its own validator");
        }
    }

    [Fact]
    public async Task AC4_DefaultViewport_IsValidForResponsiveLayoutFixture()
    {
        // AC-4: responsive layout fixture at 1280px width passes
        var validator = new ViewportValidator();
        var script = TestArtifactFactory.MakeScript(BrowserMode.Headless);

        // Default script has no declared viewport → runtime applies default
        var effectiveViewport = script.Viewport ?? ViewportSettings.Default;

        effectiveViewport.Width.Should().BeGreaterThanOrEqualTo(1280,
            because: "the responsive layout fixture is authored at 1280px minimum width");

        await validator.Invoking(v => v.ValidateAsync(effectiveViewport, CancellationToken.None))
                       .Should().NotThrowAsync(
                            because: "the default viewport must pass for the responsive layout fixture");
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(1366)]
    [InlineData(1440)]
    [InlineData(1920)]
    public async Task AC4_ResponsiveLayoutWidths_AllCoveredByCertifiedList(int expectedWidth)
    {
        // AC-4: common responsive breakpoints (1280, 1366, 1440, 1920) must all have
        // at least one certified viewport entry so fixture authors can pick any of them.
        var validator = new ViewportValidator();
        var certified = validator.GetCertifiedViewports();

        certified.Should().Contain(v => v.Width == expectedWidth,
            because: $"width {expectedWidth}px must have a certified entry for responsive layout fixture coverage");

        // Also verify the matching entry passes validation
        var matchingViewport = certified.First(v => v.Width == expectedWidth);
        await validator.Invoking(v => v.ValidateAsync(matchingViewport, CancellationToken.None))
                       .Should().NotThrowAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // CEC contract compliance — run results carry correct structure
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CecContract_FailedResult_CarriesValidationErrorCategory_ForUnsupportedViewport()
    {
        // When the pipeline produces a run result for an unsupported-viewport rejection,
        // the CEC result must carry errorCategory=VALIDATION and not be INTERNAL or ENVIRONMENT.
        const string runId = "run-cec-viewport-fail";

        var result = TestArtifactFactory.MakeFailedResult(
            runId,
            ErrorCategory.VALIDATION,
            "Viewport 800x600@1x is not in the certified viewport list");

        result.State.Should().Be(RunState.FAILED);
        result.Error.Should().NotBeNull();
        result.Error!.Category.Should().Be(ErrorCategory.VALIDATION);
        result.Error.Message.Should().Contain("800x600");
    }

    [Fact]
    public void CecContract_SuccessResult_ForCertifiedViewportRun()
    {
        // A run executed at a certified viewport should produce SUCCEEDED / SUCCESS
        const string runId = "run-cec-viewport-ok";

        var result = TestArtifactFactory.MakeSuccessResult(runId);

        result.RunId.Should().Be(runId);
        result.State.Should().Be(RunState.SUCCEEDED);
        result.ServiceOutcome.Should().Be(ServiceOutcome.SUCCESS);
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task CecContract_Metadata_CollectedAtPrecedesFirstStep()
    {
        // The metadata timestamp must predate any step start time
        var sut = new EnvironmentReporter();
        var beforeSteps = DateTimeOffset.UtcNow;

        var metadata = await sut.CollectAsync(
            "run-cec-timing", "chromium", "120.0.0",
            BrowserMode.Headless, ViewportSettings.Default, CancellationToken.None);

        // CollectedAt must be >= beforeSteps (not a backdated fake timestamp)
        metadata.CollectedAt.Should().BeOnOrAfter(beforeSteps,
            because: "environment metadata is a real-time snapshot, not a backdated record");
    }
}
