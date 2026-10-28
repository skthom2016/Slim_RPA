using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Moq;
using TractoRuntime.Browser.Session;
using TractoRuntime.Browser.Tests.Fixtures;

namespace TractoRuntime.Browser.Tests;

/// <summary>
/// Acceptance tests for BRW-US-014 — Cookie and Session State Management.
/// Covers BRW-AT-13 (cross-run isolation), BRW-AT-18 (storageState secret handling),
/// and BRW-AT-27 (secrets absent from diagnostics).
/// Requirement refs: WEB-008, WEB-009, BRE-006.
/// </summary>
/// <remarks>
/// AC#1 — Fresh run does not inherit cookies or storageState from a previous run
///         (tested in BRW_US_003_Tests; BrowserContextManager.AcquireContextAsync
///          always creates a fresh context by default).
/// AC#2 — Pre-provisioned storageState approved for reuse is referenced by a signed
///         secret reference; the raw token value is never embedded in the script, trace,
///         evidence, or Orchestrator payload.
/// AC#3 — Authorization headers, password fields, and session cookies are redacted from
///         all diagnostic evidence (screenshots, trace bodies, network summaries).
/// AC#4 — A signed diagnostic artifact produced during a failed run contains no live
///         session tokens.
/// AC#5 — When an approved storageState reference contains a refresh token, the token
///         does not appear in traces, telemetry, or log output.
/// AC#6 — When an approved session reference expires mid-run, the runtime detects the
///         expiry and stops the run with needsAttention=true or classified FAILED.
/// </remarks>
public sealed class BRW_US_014_Tests : BrowserTestBase
{
    // ────────────────────────────────────────────────────────────────────────────
    // AC#2 / AC#4 / AC#5 — SessionStore: opaque reference; raw value never exposed
    // BRW-AT-18: storageState secret reference handling
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC2_SessionStore_Save_ReturnsOpaqueRef_NotRawJson()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        var sut = new SessionStore(tempDir.Path, NullLogger<SessionStore>.Instance);

        const string storageStateJson = """{"cookies":[{"name":"session","value":"abc123"}],"origins":[]}""";

        // Act
        string sessionRef = await sut.SaveStorageStateAsync("run-ac2", storageStateJson, default);

        // Assert — ref must be opaque (GUID-like), not the raw JSON
        sessionRef.Should().NotBeNullOrWhiteSpace("AC#2: a reference must be returned");
        sessionRef.Should().NotContain("cookies",
            "AC#2: the returned ref must be opaque — it must not embed or echo the raw JSON value");
        sessionRef.Should().NotContain("session",
            "AC#2: session cookie values must never appear in the returned reference");
        sessionRef.Should().NotContain("abc123",
            "AC#2: raw token values must never appear in the returned reference");
    }

    [Fact]
    public async Task AC2_SessionStore_Save_ThenLoad_ReturnsOriginalJson()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        var sut = new SessionStore(tempDir.Path, NullLogger<SessionStore>.Instance);

        const string storageStateJson = """{"cookies":[{"name":"session","value":"tok999"}],"origins":[]}""";

        // Act
        string sessionRef = await sut.SaveStorageStateAsync("run-roundtrip", storageStateJson, default);
        string? loaded = await sut.LoadStorageStateAsync(sessionRef, default);

        // Assert — the round-tripped value is the original, proving encryption/decryption works
        loaded.Should().Be(storageStateJson,
            "AC#2: the storageState must survive a save/load round-trip through the session store");
    }

    [Fact]
    public async Task AC2_SessionStore_Load_UnknownRef_ReturnsNull()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        var sut = new SessionStore(tempDir.Path, NullLogger<SessionStore>.Instance);

        // Act
        string? result = await sut.LoadStorageStateAsync("non-existent-ref-000", default);

        // Assert — missing reference yields null (caller treats as no approved profile)
        result.Should().BeNull(
            "AC#2: an unknown session reference must return null rather than throw; " +
            "caller decides whether to treat this as an expired/invalid reference (AC#6)");
    }

    [Fact]
    public async Task AC4_SessionStore_Delete_RemovesBlobAndKey()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        var sut = new SessionStore(tempDir.Path, NullLogger<SessionStore>.Instance);

        const string json = """{"cookies":[],"origins":[]}""";
        string sessionRef = await sut.SaveStorageStateAsync("run-delete-ac4", json, default);

        // Confirm the ref loads before deletion
        (await sut.LoadStorageStateAsync(sessionRef, default)).Should().NotBeNull();

        // Act
        await sut.DeleteAsync(sessionRef, default);

        // Assert — after deletion the ref resolves to null (blob is gone; AC#4)
        string? afterDelete = await sut.LoadStorageStateAsync(sessionRef, default);
        afterDelete.Should().BeNull(
            "AC#4: after deletion the signed diagnostic artifact must contain no live session tokens — " +
            "the store must not return any data for a deleted reference");
    }

    [Fact]
    public async Task AC5_SessionStore_TwoDistinctRuns_GetDistinctRefs()
    {
        // Arrange — AC#5: each run's token must stay isolated; separate refs prevent cross-run leakage
        using var tempDir = new TempDirectory();
        var sut = new SessionStore(tempDir.Path, NullLogger<SessionStore>.Instance);

        const string jsonA = """{"cookies":[{"name":"sess","value":"run-a-token"}]}""";
        const string jsonB = """{"cookies":[{"name":"sess","value":"run-b-token"}]}""";

        // Act
        string refA = await sut.SaveStorageStateAsync("run-A", jsonA, default);
        string refB = await sut.SaveStorageStateAsync("run-B", jsonB, default);

        // Assert — refs are distinct (no cross-contamination)
        refA.Should().NotBe(refB,
            "AC#5: two runs must receive distinct session references; tokens must not be shared");

        // Each ref loads only its own value
        (await sut.LoadStorageStateAsync(refA, default)).Should().Be(jsonA);
        (await sut.LoadStorageStateAsync(refB, default)).Should().Be(jsonB);
    }

    [Fact]
    public async Task AC5_SessionStore_TamperedBlob_ReturnsNull()
    {
        // Arrange — simulate a tampered or corrupted blob
        using var tempDir = new TempDirectory();
        var sut = new SessionStore(tempDir.Path, NullLogger<SessionStore>.Instance);

        const string json = """{"cookies":[{"name":"refresh","value":"rt-secret"}],"origins":[]}""";
        string sessionRef = await sut.SaveStorageStateAsync("run-tamper", json, default);

        // Corrupt the blob file by overwriting with random bytes
        string blobPath = Path.Combine(tempDir.Path, $"{sessionRef}.blob");
        await File.WriteAllBytesAsync(blobPath, new byte[64]);

        // Act — loading a tampered blob should return null gracefully (AC#5 / AC#6)
        string? result = await sut.LoadStorageStateAsync(sessionRef, default);

        // Assert — corrupted/tampered blob must not throw; treated as expired/invalid
        result.Should().BeNull(
            "AC#5: a tampered storageState blob must be treated as invalid; " +
            "the raw token must never be returned or exposed");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // AC#3 / AC#4 — EvidenceRedactor: sensitive values absent from evidence
    // BRW-AT-27: secrets absent from all diagnostics
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AC3_EvidenceRedactor_RedactHeaders_AuthorizationHeader_IsRedacted()
    {
        // Arrange
        var sut = new EvidenceRedactor(NullLogger<EvidenceRedactor>.Instance);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"]    = "Bearer eyJhbGciOiJSUzI1NiJ9.secret-payload",
            ["Content-Type"]     = "application/json",
            ["X-Request-Id"]     = "req-abc-123"
        };

        // Act
        var redacted = sut.RedactHeaders(headers);

        // Assert — Authorization header value must be masked (AC#3 / BRE-006)
        redacted["Authorization"].Should().Be("[REDACTED]",
            "AC#3: Authorization headers must never appear in diagnostic evidence");
        redacted["Content-Type"].Should().Be("application/json",
            "non-sensitive headers must pass through unchanged");
        redacted["X-Request-Id"].Should().Be("req-abc-123",
            "correlation IDs are not credentials and must be preserved");
    }

    [Fact]
    public void AC3_EvidenceRedactor_RedactHeaders_CookieHeader_IsRedacted()
    {
        // Arrange
        var sut = new EvidenceRedactor(NullLogger<EvidenceRedactor>.Instance);

        var headers = new Dictionary<string, string>
        {
            ["Cookie"]    = "session=abc; auth=xyz",
            ["Set-Cookie"] = "session=new-token; Path=/; HttpOnly",
            ["Host"]      = "app.example.com"
        };

        // Act
        var redacted = sut.RedactHeaders(headers);

        // Assert
        redacted["Cookie"].Should().Be("[REDACTED]",
            "AC#3: session cookies in request headers must be redacted");
        redacted["Set-Cookie"].Should().Be("[REDACTED]",
            "AC#3: session cookies in response Set-Cookie headers must be redacted");
        redacted["Host"].Should().Be("app.example.com",
            "the Host header is not a credential and must be preserved");
    }

    [Fact]
    public void AC3_EvidenceRedactor_RedactEvidence_SensitiveKeys_AreRedacted()
    {
        // Arrange
        var sut = new EvidenceRedactor(NullLogger<EvidenceRedactor>.Instance);

        var evidence = new Dictionary<string, object?>
        {
            ["sessionToken"]    = "live-session-token-value",
            ["authorizationHeader"] = "Bearer secret",
            ["requestUrl"]      = "https://app.example.com/api/endpoint",
            ["statusCode"]      = 200,
            ["password"]        = "s3cr3t",
            ["refreshToken"]    = "rt-live-value"
        };

        // Act
        var redacted = sut.RedactEvidence(evidence);

        // Assert — all credential keys must be masked
        redacted["sessionToken"].Should().Be("[REDACTED]",
            "AC#3: session tokens must not appear in diagnostic evidence");
        redacted["authorizationHeader"].Should().Be("[REDACTED]",
            "AC#3: authorization header values must not appear in evidence");
        redacted["password"].Should().Be("[REDACTED]",
            "AC#3: password field values must not appear in evidence");
        redacted["refreshToken"].Should().Be("[REDACTED]",
            "AC#5: refresh tokens must not appear in traces or telemetry");

        // Non-sensitive fields must pass through
        redacted["requestUrl"].Should().Be("https://app.example.com/api/endpoint");
        redacted["statusCode"].Should().Be(200);
    }

    [Fact]
    public void AC4_EvidenceRedactor_RedactStorageState_StorageStateJson_IsFullyRedacted()
    {
        // Arrange
        var sut = new EvidenceRedactor(NullLogger<EvidenceRedactor>.Instance);

        const string rawStorageState =
            """{"cookies":[{"name":"session","value":"live-token"}],"origins":[]}""";

        // Act — AC#4: a storageState value appearing in trace bodies must be redacted
        string result = sut.RedactStorageState(rawStorageState);

        // Assert
        result.Should().Be("[REDACTED:storageState]",
            "AC#4: any storageState JSON (containing live session tokens) " +
            "must be fully replaced in signed diagnostic artifacts");
        result.Should().NotContain("live-token",
            "AC#4: live session tokens must never appear in signed diagnostic artifacts");
    }

    [Fact]
    public void AC4_EvidenceRedactor_RedactStorageState_NonStorageStateJson_PassesThrough()
    {
        // Arrange
        var sut = new EvidenceRedactor(NullLogger<EvidenceRedactor>.Instance);

        const string safeJson = """{"requestId":"abc123","duration":500}""";

        // Act
        string result = sut.RedactStorageState(safeJson);

        // Assert — non-session JSON should not be modified
        result.Should().Be(safeJson,
            "non-storageState JSON without cookies/origins must not be redacted unnecessarily");
    }

    [Fact]
    public void AC3_EvidenceRedactor_IsScreenshotSafe_PasswordPage_ReturnsFalse()
    {
        // Arrange
        var sut = new EvidenceRedactor(NullLogger<EvidenceRedactor>.Instance);

        // Act — AC#3: screenshot taken on a page with password fields
        bool safe = sut.IsScreenshotSafe(
            pageContainsPasswordFields: true,
            pageContainsSessionCookies: false,
            out string? reason);

        // Assert
        safe.Should().BeFalse(
            "AC#3: screenshots on pages with password fields must be flagged as unsafe; " +
            "a non-image artifact must be substituted (BRE-006)");
        reason.Should().NotBeNullOrWhiteSpace(
            "a human-readable reason must be provided when a screenshot is unsafe");
    }

    [Fact]
    public void AC3_EvidenceRedactor_IsScreenshotSafe_SessionCookiePage_ReturnsFalse()
    {
        // Arrange
        var sut = new EvidenceRedactor(NullLogger<EvidenceRedactor>.Instance);

        // Act
        bool safe = sut.IsScreenshotSafe(
            pageContainsPasswordFields: false,
            pageContainsSessionCookies: true,
            out string? reason);

        // Assert
        safe.Should().BeFalse(
            "AC#3: screenshots on pages rendering session-authenticated content must be flagged unsafe");
        reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void AC3_EvidenceRedactor_IsScreenshotSafe_NeutralPage_ReturnsTrue()
    {
        // Arrange
        var sut = new EvidenceRedactor(NullLogger<EvidenceRedactor>.Instance);

        // Act — page has neither password fields nor session cookies
        bool safe = sut.IsScreenshotSafe(
            pageContainsPasswordFields: false,
            pageContainsSessionCookies: false,
            out string? reason);

        // Assert
        safe.Should().BeTrue(
            "a page without password fields or session cookies may be safely screenshot'd");
        reason.Should().BeNull(
            "no redaction reason should be set for a safe screenshot");
    }

    [Fact]
    public void AC5_EvidenceRedactor_BearerTokenInEvidenceValue_IsRedacted()
    {
        // Arrange — a non-sensitive key whose string value is a Bearer token (AC#5)
        var sut = new EvidenceRedactor(NullLogger<EvidenceRedactor>.Instance);

        var evidence = new Dictionary<string, object?>
        {
            ["requestHeaders.x-custom"] = "Bearer eyJhbGciOiJSUzI1NiJ9.payload.sig",
            ["traceId"]                 = "trace-abc-123"
        };

        // Act
        var redacted = sut.RedactEvidence(evidence);

        // Assert — Bearer token values must be redacted even when the key is not sensitive (AC#5)
        redacted["requestHeaders.x-custom"].Should().Be("[REDACTED]",
            "AC#5: Bearer token values must be redacted regardless of the key name; " +
            "refresh tokens must not appear in traces or telemetry");
        redacted["traceId"].Should().Be("trace-abc-123");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // AC#6 — SessionExpiryDetector: expiry detected; no silent re-authentication
    // BRW-AT-27: session expiry stops run with needsAttention
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC6_SessionExpiryDetector_LoginRedirectUrl_DetectedAsExpired()
    {
        // Arrange — simulate Playwright page whose URL is a login redirect
        var pageMock = new Mock<IPage>();
        pageMock.Setup(p => p.Url).Returns("https://app.example.com/login?returnUrl=/dashboard");
        pageMock.Setup(p => p.TitleAsync()).ReturnsAsync("Sign In – MyApp");

        var sut = new SessionExpiryDetector(NullLogger<SessionExpiryDetector>.Instance);

        // Act
        bool expired = await sut.IsExpiredAsync(pageMock.Object, sessionRef: "ref-ac6-login", default);

        // Assert — URL pattern match signals session expiry (AC#6 / WEB-009)
        expired.Should().BeTrue(
            "AC#6: navigating to a login page mid-run indicates the session has expired");
    }

    [Fact]
    public async Task AC6_SessionExpiryDetector_SessionExpiredUrl_DetectedAsExpired()
    {
        // Arrange
        var pageMock = new Mock<IPage>();
        pageMock.Setup(p => p.Url).Returns("https://app.example.com/session-expired");
        pageMock.Setup(p => p.TitleAsync()).ReturnsAsync("Session Expired");

        var sut = new SessionExpiryDetector(NullLogger<SessionExpiryDetector>.Instance);

        // Act
        bool expired = await sut.IsExpiredAsync(pageMock.Object, "ref-ac6-url", default);

        // Assert
        expired.Should().BeTrue(
            "AC#6: a /session-expired URL indicates the session has expired mid-run");
    }

    [Fact]
    public async Task AC6_SessionExpiryDetector_PageTitleContainsExpiry_DetectedAsExpired()
    {
        // Arrange — URL looks normal but page title signals expiry
        var pageMock = new Mock<IPage>();
        pageMock.Setup(p => p.Url).Returns("https://app.example.com/dashboard");
        pageMock.Setup(p => p.TitleAsync()).ReturnsAsync("Session Expired – Please Log In Again");
        pageMock.Setup(p => p.Locator(It.IsAny<string>()))
                .Returns(Mock.Of<ILocator>(l =>
                    l.InnerTextAsync(It.IsAny<LocatorInnerTextOptions?>()) ==
                    Task.FromResult("Your session has expired. Please sign in again.")));

        var sut = new SessionExpiryDetector(NullLogger<SessionExpiryDetector>.Instance);

        // Act
        bool expired = await sut.IsExpiredAsync(pageMock.Object, "ref-ac6-title", default);

        // Assert
        expired.Should().BeTrue(
            "AC#6: a page title containing session expiry text must be detected");
    }

    [Fact]
    public async Task AC6_SessionExpiryDetector_ActiveSession_ReturnsFalse()
    {
        // Arrange — page looks like a healthy authenticated dashboard
        var pageMock = new Mock<IPage>();
        pageMock.Setup(p => p.Url).Returns("https://app.example.com/dashboard");
        pageMock.Setup(p => p.TitleAsync()).ReturnsAsync("Dashboard – MyApp");
        pageMock.Setup(p => p.Locator(It.IsAny<string>()))
                .Returns(Mock.Of<ILocator>(l =>
                    l.InnerTextAsync(It.IsAny<LocatorInnerTextOptions?>()) ==
                    Task.FromResult("Welcome back, user!")));

        var sut = new SessionExpiryDetector(NullLogger<SessionExpiryDetector>.Instance);

        // Act
        bool expired = await sut.IsExpiredAsync(pageMock.Object, "ref-active", default);

        // Assert — active session must not be misclassified as expired
        expired.Should().BeFalse(
            "AC#6: a healthy authenticated page must not be detected as expired");
    }

    [Fact]
    public async Task AC6_SessionExpiryDetector_ThrowIfExpired_ExpiredSession_ThrowsWithNeedsAttention()
    {
        // Arrange — expired session URL
        var pageMock = new Mock<IPage>();
        pageMock.Setup(p => p.Url).Returns("https://idp.example.com/login?reason=expired");
        pageMock.Setup(p => p.TitleAsync()).ReturnsAsync("Sign In");

        var sut = new SessionExpiryDetector(NullLogger<SessionExpiryDetector>.Instance);

        // Act & Assert — AC#6: the run must stop with needsAttention=true; no silent re-auth
        var ex = await Assert.ThrowsAsync<SessionExpiryException>(
            () => sut.ThrowIfExpiredAsync(pageMock.Object, "ref-throw-ac6", default));

        ex.NeedsAttention.Should().BeTrue(
            "AC#6: session expiry requires operator attention to provision a fresh credential; " +
            "the runtime must NOT attempt silent re-authentication");
        ex.SessionRef.Should().Be("ref-throw-ac6",
            "the opaque reference must be carried in the exception for log correlation");
        ex.Message.Should().NotContain("abc",
            "the exception message must not contain any raw token value");
    }

    [Fact]
    public async Task AC6_SessionExpiryDetector_ThrowIfExpired_ActiveSession_DoesNotThrow()
    {
        // Arrange — healthy session
        var pageMock = new Mock<IPage>();
        pageMock.Setup(p => p.Url).Returns("https://app.example.com/orders");
        pageMock.Setup(p => p.TitleAsync()).ReturnsAsync("Orders – MyApp");
        pageMock.Setup(p => p.Locator(It.IsAny<string>()))
                .Returns(Mock.Of<ILocator>(l =>
                    l.InnerTextAsync(It.IsAny<LocatorInnerTextOptions?>()) ==
                    Task.FromResult("Order #12345: Processing")));

        var sut = new SessionExpiryDetector(NullLogger<SessionExpiryDetector>.Instance);

        // Act & Assert — active session must not cause the method to throw
        var act = () => sut.ThrowIfExpiredAsync(pageMock.Object, "ref-no-throw", default);

        await act.Should().NotThrowAsync(
            "AC#6: an active session must not produce a SessionExpiryException");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // SessionExpiryException — unit tests for the exception type
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SessionExpiryException_DefaultsToNeedsAttentionTrue()
    {
        var ex = new SessionExpiryException("session expired", sessionRef: "ref-123");

        ex.NeedsAttention.Should().BeTrue(
            "the default is needsAttention=true because most expiry cases require operator action");
        ex.SessionRef.Should().Be("ref-123");
    }

    [Fact]
    public void SessionExpiryException_NeedsAttentionFalse_CanBeSet()
    {
        var ex = new SessionExpiryException(
            "session invalid — FAILED",
            sessionRef: "ref-456",
            needsAttention: false);

        ex.NeedsAttention.Should().BeFalse();
    }

    [Fact]
    public void SessionExpiryException_InnerException_IsPreserved()
    {
        var inner = new InvalidOperationException("inner cause");
        var ex = new SessionExpiryException("outer", "ref-789", inner);

        ex.InnerException.Should().BeSameAs(inner);
    }
}

/// <summary>Disposable helper that creates and cleans up a temp directory for test file I/O.</summary>
file sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        $"tracto-brw-us014-{Guid.NewGuid():N}");

    public TempDirectory() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}
