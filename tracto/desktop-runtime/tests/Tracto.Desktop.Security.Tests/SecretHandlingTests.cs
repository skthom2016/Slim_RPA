using System.Reflection;
using System.Text;
using FluentAssertions;
using Tracto.Desktop.Element;
using Tracto.Desktop.Input;
using Tracto.Desktop.Security;
using Tracto.Desktop.Session;
using Xunit;

namespace Tracto.Desktop.Security.Tests;

/// <summary>
/// DT-11 — DPAPI Secret Handling unit tests.
/// </summary>
public sealed class SecretHandlingTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a mock DPAPI-like blob: the plaintext UTF-8 bytes as-is (used with
    /// MockDpapiResolver which skips actual DPAPI).
    /// </summary>
    private static MockCredentialStore BuildStore(string key, string plaintext)
    {
        var store = new MockCredentialStore();
        store.SetPlaintext(key, plaintext);
        return store;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. DpapiSecretResolver — non-interactive session → POLICY_DENIED
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DpapiSecretResolver_NonInteractiveSession_ThrowsPolicyDenied_BeforeDecryption()
    {
        // Arrange
        var store = new CallCountingCredentialStore();
        var session = new MockSessionStateProvider { State = SessionState.NonInteractive };
        var resolver = new DpapiSecretResolver(store, session);
        var secret = new SecretRef("any.key");

        // Act
        var act = () => resolver.ResolveAsync(secret, CancellationToken.None);

        // Assert: throws before GetEncryptedBlob is ever called
        await act.Should().ThrowAsync<DesktopActionError>()
            .WithMessage("*POLICY_DENIED*");
        store.CallCount.Should().Be(0, "GetEncryptedBlob must NOT be called before the policy check");
    }

    [Theory]
    [InlineData(SessionState.Locked)]
    [InlineData(SessionState.Disconnected)]
    [InlineData(SessionState.NonInteractive)]
    public async Task DpapiSecretResolver_NonInteractiveStates_AllThrowPolicyDenied(SessionState state)
    {
        var store = new CallCountingCredentialStore();
        var session = new MockSessionStateProvider { State = state };
        var resolver = new DpapiSecretResolver(store, session);

        await Assert.ThrowsAsync<DesktopActionError>(
            () => resolver.ResolveAsync(new SecretRef("k"), CancellationToken.None));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. ClearableBuffer — after Dispose, all chars zeroed (0x0000)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ClearableBuffer_AfterDispose_AllCharsAreZero()
    {
        // Arrange
        var buf = new ClearableBuffer(8);

        // Get the internal char[] reference BEFORE Dispose via reflection.
        var field = typeof(ClearableBuffer)
            .GetField("_buffer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.Should().NotBeNull("_buffer field must exist for this test");
        var internalArray = (char[])field.GetValue(buf)!;

        // Write non-zero data directly into the internal array.
        internalArray[0] = 'S';
        internalArray[1] = 'E';
        internalArray[2] = 'C';

        // Act
        buf.Dispose();

        // Assert: Array.Clear was called on the same array object before it was nulled.
        internalArray.Should().AllSatisfy(c => c.Should().Be('\0'),
            "all chars must be zeroed after Dispose");
    }

    [Fact]
    public void ClearableBuffer_SpanAfterDispose_ThrowsObjectDisposedException()
    {
        var buf = new ClearableBuffer(4);
        buf.Dispose();

        var act = () => { _ = buf.Span; };
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void ClearableBuffer_DoubleDispose_IsIdempotent()
    {
        var buf = new ClearableBuffer(4);
        buf.Dispose();
        var act = buf.Dispose;  // second Dispose must not throw
        act.Should().NotThrow();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. EvidenceSanitizer
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EvidenceSanitizer_ContainsPattern_TrueWhenSecretPresent()
    {
        const string secret = "TEST_SECRET_12345";
        var text = $"User typed {secret} into the field.";

        EvidenceSanitizer.ContainsPattern(text, secret).Should().BeTrue();
    }

    [Fact]
    public void EvidenceSanitizer_ContainsPattern_FalseWhenSecretAbsent()
    {
        EvidenceSanitizer.ContainsPattern("nothing here", "TEST_SECRET_12345").Should().BeFalse();
    }

    [Fact]
    public void EvidenceSanitizer_Redact_ReplacesSecretWithStars()
    {
        const string secret = "TEST_SECRET_12345";
        var text = $"Input: {secret} end";

        var result = EvidenceSanitizer.Redact(text, secret);

        result.Should().NotContain(secret);
        result.Should().Contain("***");
    }

    [Fact]
    public void EvidenceSanitizer_Redact_MultipleOccurrences_AllReplaced()
    {
        const string secret = "TEST_SECRET_12345";
        var text = $"{secret} and {secret}";

        var result = EvidenceSanitizer.Redact(text, secret);

        result.Should().NotContain(secret);
        result.Split("***").Length.Should().BeGreaterThan(2, "both occurrences should be redacted");
    }

    [Fact]
    public void EvidenceSanitizer_Redact_NoOccurrences_ReturnsSameText()
    {
        const string text = "no secrets here";
        EvidenceSanitizer.Redact(text, "TEST_SECRET_12345").Should().Be(text);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. SecretApplicationBridge — buffer is disposed after ApplySecretAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SecretApplicationBridge_AfterApply_BufferIsDisposed()
    {
        // Arrange: a resolver that captures the buffer it creates.
        var capturingResolver = new CapturingSecretResolver("hello");
        var kb = new NoOpKeyboardInputEngine();
        var target = FakeElementRef.Create();
        var secret = new SecretRef("any");

        // Act
        await SecretApplicationBridge.ApplySecretAsync(secret, target, kb, capturingResolver, CancellationToken.None);

        // Assert: the buffer created by the resolver is disposed.
        capturingResolver.LastBuffer.Should().NotBeNull();
        var act = () => { _ = capturingResolver.LastBuffer!.Span; };
        act.Should().Throw<ObjectDisposedException>("buffer must be disposed after ApplySecretAsync");
    }

    [Fact]
    public async Task SecretApplicationBridge_WhenResolverThrows_BufferIsStillDisposed()
    {
        var resolver = new ThrowingAfterResolveResolver("secret");
        var kb = new NoOpKeyboardInputEngine();
        var target = FakeElementRef.Create();

        // The resolver provides a buffer but the keyboard throws — buffer must still be disposed.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => SecretApplicationBridge.ApplySecretAsync(
                new SecretRef("k"), target, new ThrowingKeyboardInputEngine(), resolver, CancellationToken.None));

        var act = () => { _ = resolver.LastBuffer!.Span; };
        act.Should().Throw<ObjectDisposedException>("buffer must be disposed even when typing fails");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. SessionBoundaryGuard
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SessionBoundaryGuard_NonInteractive_ThrowsPolicyDenied()
    {
        var session = new MockSessionStateProvider { State = SessionState.NonInteractive };

        var act = () => SessionBoundaryGuard.ThrowIfNotInteractive(session);

        act.Should().Throw<DesktopActionError>()
            .Which.ErrorCode.Should().Be("POLICY_DENIED");
    }

    [Fact]
    public void SessionBoundaryGuard_Interactive_DoesNotThrow()
    {
        var session = new MockSessionStateProvider { State = SessionState.Interactive };

        var act = () => SessionBoundaryGuard.ThrowIfNotInteractive(session);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(SessionState.Locked)]
    [InlineData(SessionState.Disconnected)]
    [InlineData(SessionState.NonInteractive)]
    public void SessionBoundaryGuard_AnyNonInteractiveState_ThrowsPolicyDenied(SessionState state)
    {
        var session = new MockSessionStateProvider { State = state };
        Assert.Throws<DesktopActionError>(() => SessionBoundaryGuard.ThrowIfNotInteractive(session));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test doubles
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Credential store that counts calls to GetEncryptedBlob.</summary>
    private sealed class CallCountingCredentialStore : ICredentialStore
    {
        public int CallCount { get; private set; }
        public byte[] GetEncryptedBlob(string key)
        {
            CallCount++;
            return Array.Empty<byte>();
        }
    }

    /// <summary>Resolver that captures the ClearableBuffer it returns.</summary>
    private sealed class CapturingSecretResolver : ISecretResolver
    {
        private readonly string _plaintext;
        public ClearableBuffer? LastBuffer { get; private set; }

        public CapturingSecretResolver(string plaintext) => _plaintext = plaintext;

        public Task<ClearableBuffer> ResolveAsync(SecretRef secretRef, CancellationToken ct)
        {
            var chars = _plaintext.ToCharArray();
            var buf = new ClearableBuffer(chars.Length > 0 ? chars.Length : 1);
            chars.AsSpan().CopyTo(buf.Span);
            LastBuffer = buf;
            return Task.FromResult(buf);
        }
    }

    /// <summary>Resolver that captures the buffer but the keyboard engine will throw.</summary>
    private sealed class ThrowingAfterResolveResolver : ISecretResolver
    {
        private readonly string _plaintext;
        public ClearableBuffer? LastBuffer { get; private set; }

        public ThrowingAfterResolveResolver(string plaintext) => _plaintext = plaintext;

        public Task<ClearableBuffer> ResolveAsync(SecretRef secretRef, CancellationToken ct)
        {
            var chars = _plaintext.ToCharArray();
            var buf = new ClearableBuffer(chars.Length > 0 ? chars.Length : 1);
            chars.AsSpan().CopyTo(buf.Span);
            LastBuffer = buf;
            return Task.FromResult(buf);
        }
    }

    /// <summary>Keyboard engine that does nothing (no-op) for secret typing tests.</summary>
    private sealed class NoOpKeyboardInputEngine : IKeyboardInputEngine
    {
        public Task<TypeResult> TypeAsync(TypeOptions options, CancellationToken ct = default)
            => Task.FromResult(new TypeResult(TypeStrategy.Keystroke, true, null));

        public Task PressAsync(string chord, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task KeyDownUpAsync(string key, int holdMs, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    /// <summary>Keyboard engine that always throws to test buffer disposal on failure.</summary>
    private sealed class ThrowingKeyboardInputEngine : IKeyboardInputEngine
    {
        public Task<TypeResult> TypeAsync(TypeOptions options, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated keyboard failure");

        public Task PressAsync(string chord, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task KeyDownUpAsync(string key, int holdMs, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    /// <summary>Creates a fake ElementRef for use in tests (avoids UIA dependency).</summary>
    private static class FakeElementRef
    {
        public static ElementRef Create()
        {
            // AutomationElement.RootElement is always available on Windows.
            var automationElement = System.Windows.Automation.AutomationElement.RootElement;
            var selector = new Tracto.Desktop.Element.ElementSelector
            {
                AutomationId = "test",
                Name = "test"
            };
            return new ElementRef(automationElement, selector);
        }
    }
}
