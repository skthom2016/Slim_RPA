using FluentAssertions;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Environment;
using TractoRuntime.Browser.Viewport;

namespace TractoRuntime.Browser.Tests.Environment;

/// <summary>
/// Tests for BRW-US-006 AC-1 / AC-3 / BRW-006:
/// Environment metadata is collected with all required fields and no credentials.
/// </summary>
public sealed class EnvironmentReporterTests
{
    private const string RunId          = "run-brw006-test";
    private const string BrowserEngine  = "chromium";
    private const string BrowserVersion = "120.0.6099.71";

    private static readonly ViewportSettings DefaultViewport = ViewportSettings.Default;

    // -----------------------------------------------------------------------
    // CollectAsync — all required fields are present
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CollectAsync_PopulatesAllRequiredFields()
    {
        var sut = new EnvironmentReporter(
            locale:          "en-US",
            timezone:        "UTC",
            tlsConfigRef:    "tls-policy-ref-001",
            proxyConfigRef:  "proxy-policy-ref-001",
            capabilityDigest:"sha256:abc123");

        var metadata = await sut.CollectAsync(
            RunId, BrowserEngine, BrowserVersion,
            BrowserMode.Headless, DefaultViewport,
            CancellationToken.None);

        metadata.RunId.Should().Be(RunId);
        metadata.BrowserEngine.Should().Be(BrowserEngine);
        metadata.BrowserVersion.Should().Be(BrowserVersion);
        metadata.ExecutionMode.Should().Be(BrowserMode.Headless);
        metadata.Viewport.Should().Be(DefaultViewport);
        metadata.Locale.Should().Be("en-US");
        metadata.Timezone.Should().Be("UTC");
        metadata.TlsConfigRef.Should().Be("tls-policy-ref-001");
        metadata.ProxyConfigRef.Should().Be("proxy-policy-ref-001");
        metadata.CapabilityDigest.Should().Be("sha256:abc123");
        metadata.CollectedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CollectAsync_HeadedMode_ReportsHeadedMode()
    {
        var sut = new EnvironmentReporter();

        var metadata = await sut.CollectAsync(
            RunId, BrowserEngine, BrowserVersion,
            BrowserMode.Headed, DefaultViewport,
            CancellationToken.None);

        metadata.ExecutionMode.Should().Be(BrowserMode.Headed);
    }

    [Fact]
    public async Task CollectAsync_NullConfigRefs_AreAllowed()
    {
        var sut = new EnvironmentReporter();

        var metadata = await sut.CollectAsync(
            RunId, BrowserEngine, BrowserVersion,
            BrowserMode.Headless, DefaultViewport,
            CancellationToken.None);

        // Null config refs are valid when no TLS/proxy policy is configured
        metadata.TlsConfigRef.Should().BeNull();
        metadata.ProxyConfigRef.Should().BeNull();
        metadata.CapabilityDigest.Should().BeNull();
    }

    [Fact]
    public async Task CollectAsync_ReturnsViewportWithDeclaredDimensions()
    {
        var viewport = new ViewportSettings { Width = 1920, Height = 1080, DeviceScaleFactor = 2.0 };
        var sut = new EnvironmentReporter();

        var metadata = await sut.CollectAsync(
            RunId, BrowserEngine, BrowserVersion,
            BrowserMode.Headless, viewport,
            CancellationToken.None);

        metadata.Viewport.Width.Should().Be(1920);
        metadata.Viewport.Height.Should().Be(1080);
        metadata.Viewport.DeviceScaleFactor.Should().Be(2.0);
    }

    // -----------------------------------------------------------------------
    // BuildSanitizedEvidence — all required keys, no secrets
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildSanitizedEvidence_ContainsAllRequiredDiagnosticKeys()
    {
        var sut = new EnvironmentReporter(
            locale: "de-DE", timezone: "Europe/Berlin",
            tlsConfigRef: "tls-ref", proxyConfigRef: "proxy-ref",
            capabilityDigest: "sha256:xyz");

        var metadata = await sut.CollectAsync(
            RunId, BrowserEngine, BrowserVersion,
            BrowserMode.Headless, DefaultViewport,
            CancellationToken.None);

        var evidence = sut.BuildSanitizedEvidence(metadata);

        evidence.Should().ContainKey("runId");
        evidence.Should().ContainKey("browserEngine");
        evidence.Should().ContainKey("browserVersion");
        evidence.Should().ContainKey("executionMode");
        evidence.Should().ContainKey("viewportWidth");
        evidence.Should().ContainKey("viewportHeight");
        evidence.Should().ContainKey("deviceScaleFactor");
        evidence.Should().ContainKey("locale");
        evidence.Should().ContainKey("timezone");
        evidence.Should().ContainKey("tlsConfigRef");
        evidence.Should().ContainKey("proxyConfigRef");
        evidence.Should().ContainKey("capabilityDigest");
        evidence.Should().ContainKey("collectedAt");
    }

    [Fact]
    public async Task BuildSanitizedEvidence_ContainsCorrectValues()
    {
        var sut = new EnvironmentReporter(locale: "en-US", timezone: "UTC");

        var metadata = await sut.CollectAsync(
            RunId, BrowserEngine, BrowserVersion,
            BrowserMode.Headless, DefaultViewport,
            CancellationToken.None);

        var evidence = sut.BuildSanitizedEvidence(metadata);

        evidence["runId"].Should().Be(RunId);
        evidence["browserEngine"].Should().Be(BrowserEngine);
        evidence["browserVersion"].Should().Be(BrowserVersion);
        evidence["executionMode"].Should().Be("Headless");
        evidence["viewportWidth"].Should().Be(DefaultViewport.Width);
        evidence["viewportHeight"].Should().Be(DefaultViewport.Height);
        evidence["deviceScaleFactor"].Should().Be(DefaultViewport.DeviceScaleFactor);
        evidence["locale"].Should().Be("en-US");
        evidence["timezone"].Should().Be("UTC");
    }

    /// <summary>
    /// AC-3: Diagnostic evidence must not contain credentials, tokens, or secrets.
    /// TLS and proxy appear only as opaque reference identifiers.
    /// </summary>
    [Fact]
    public async Task BuildSanitizedEvidence_TlsAndProxyAppearAsReferenceIdentifiersOnly()
    {
        const string tlsRef   = "tls-policy-ref-007";
        const string proxyRef = "proxy-policy-ref-007";

        var sut = new EnvironmentReporter(
            tlsConfigRef: tlsRef, proxyConfigRef: proxyRef);

        var metadata = await sut.CollectAsync(
            RunId, BrowserEngine, BrowserVersion,
            BrowserMode.Headless, DefaultViewport,
            CancellationToken.None);

        var evidence = sut.BuildSanitizedEvidence(metadata);

        // The reference string itself is safe — it is an identifier, not a credential.
        // What must NOT appear is any raw certificate, key, password, or auth header.
        evidence["tlsConfigRef"].Should().Be(tlsRef);
        evidence["proxyConfigRef"].Should().Be(proxyRef);

        // The evidence values must all be non-secret (strings or numbers, no JSON blobs
        // containing PEM keys, Bearer tokens, cookies, etc.)
        foreach (var value in evidence.Values)
        {
            if (value is string s)
            {
                s.Should().NotContain("-----BEGIN",
                    because: "PEM certificate material must not appear in diagnostic evidence");
                s.Should().NotContain("Bearer ",
                    because: "Bearer tokens must not appear in diagnostic evidence");
            }
        }
    }

    [Fact]
    public async Task BuildSanitizedEvidence_CollectedAtIsIso8601String()
    {
        var sut = new EnvironmentReporter();

        var metadata = await sut.CollectAsync(
            RunId, BrowserEngine, BrowserVersion,
            BrowserMode.Headless, DefaultViewport,
            CancellationToken.None);

        var evidence = sut.BuildSanitizedEvidence(metadata);

        var collectedAt = evidence["collectedAt"] as string;
        collectedAt.Should().NotBeNullOrEmpty();
        DateTimeOffset.TryParse(collectedAt, out _).Should().BeTrue(
            because: "collectedAt must be an ISO 8601 timestamp");
    }

    // -----------------------------------------------------------------------
    // Cancellation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CollectAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var sut = new EnvironmentReporter();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await sut.Invoking(r => r.CollectAsync(
                      RunId, BrowserEngine, BrowserVersion,
                      BrowserMode.Headless, DefaultViewport, cts.Token))
                 .Should().ThrowAsync<OperationCanceledException>();
    }
}
