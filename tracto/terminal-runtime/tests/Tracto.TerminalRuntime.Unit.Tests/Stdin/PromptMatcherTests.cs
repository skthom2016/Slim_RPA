using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.Stdin;
using Tracto.TerminalRuntime.Domain.Stdin;
using Tracto.TerminalRuntime.Infrastructure.Stdin;

namespace Tracto.TerminalRuntime.Unit.Tests.Stdin;

/// <summary>
/// Unit tests for TER-US-012 AC-2, AC-3, AC-4 via <see cref="PromptMatcher"/>.
///
/// The tests use:
/// <list type="bullet">
///   <item><see cref="StringReader"/> to simulate process stdout.</item>
///   <item><see cref="MemoryStream"/> to capture stdin writes.</item>
///   <item>A mock <see cref="IStdinWriter"/> to assert write behaviour without I/O.</item>
/// </list>
/// </summary>
public sealed class PromptMatcherTests
{
    // Short timeout used in tests so that "timeout" cases complete quickly.
    private static StdinPolicy FastPolicy(int timeoutMs = 200) =>
        new(MaxBytes: 65_536, PromptTimeoutMs: timeoutMs);

    // ── AC-2: Allowlisted prompt matched → approved response written ─────────

    [Fact]
    public async Task WaitForPromptAndRespondAsync_AllowlistedPrompt_WritesApprovedResponse()
    {
        // Arrange
        var allowlist = new List<PromptAllowlistEntry>
        {
            new("Enter username:", "automation_user"),
            new("Enter passphrase for key:", "APPROVED_PASSPHRASE_PLACEHOLDER")
        };

        // Stdout produces exactly one matching line then EOF.
        using var stdoutReader = new StringReader("Enter username:");
        using var stdinStream = new MemoryStream();

        var realWriter = new StdinWriter(NullLogger<StdinWriter>.Instance);
        var matcher = new PromptMatcher(realWriter, NullLogger<PromptMatcher>.Instance);

        // Act
        var result = await matcher.WaitForPromptAndRespondAsync(
            stdoutReader, stdinStream, allowlist, FastPolicy());

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify the approved response (not the secret) was written to stdin.
        stdinStream.Position = 0;
        var written = new StreamReader(stdinStream).ReadToEnd();
        written.Should().Be("automation_user\n");
    }

    [Fact]
    public async Task WaitForPromptAndRespondAsync_PromptMatchedOnSecondLine_WritesApprovedResponse()
    {
        // Stdout emits noise first, then a matching prompt.
        var allowlist = new List<PromptAllowlistEntry>
        {
            new("Continue\\? \\[y/n\\]", "y")
        };

        var stdout = "Loading configuration...\nContinue? [y/n]";
        using var stdoutReader = new StringReader(stdout);
        using var stdinStream = new MemoryStream();
        var realWriter = new StdinWriter(NullLogger<StdinWriter>.Instance);
        var matcher = new PromptMatcher(realWriter, NullLogger<PromptMatcher>.Instance);

        var result = await matcher.WaitForPromptAndRespondAsync(
            stdoutReader, stdinStream, allowlist, FastPolicy());

        result.IsSuccess.Should().BeTrue();
        stdinStream.Position = 0;
        new StreamReader(stdinStream).ReadToEnd().Should().Be("y\n");
    }

    // ── AC-3: No allowlisted prompt within timeout → TIMEOUT error ───────────

    [Fact]
    public async Task WaitForPromptAndRespondAsync_NoMatchingPromptBeforeEof_ReturnsTimeoutError()
    {
        // Stdout produces a non-matching, non-credential line then EOF.
        var allowlist = new List<PromptAllowlistEntry>
        {
            new("Enter username:", "automation_user")
        };

        // Line is not a credential prompt and does not match the allowlist.
        using var stdoutReader = new StringReader("Starting deployment...");
        using var stdinStream = new MemoryStream();
        var realWriter = new StdinWriter(NullLogger<StdinWriter>.Instance);
        var matcher = new PromptMatcher(realWriter, NullLogger<PromptMatcher>.Instance);

        var result = await matcher.WaitForPromptAndRespondAsync(
            stdoutReader, stdinStream, allowlist, FastPolicy());

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Timeout);
        result.Error.DetailCode.Should().Be("terminal.promptTimeout");
    }

    [Fact]
    public async Task WaitForPromptAndRespondAsync_EmptyStdout_ReturnsTimeoutError()
    {
        var allowlist = new List<PromptAllowlistEntry> { new("prompt:", "answer") };
        using var stdoutReader = new StringReader(string.Empty);
        using var stdinStream = new MemoryStream();
        var realWriter = new StdinWriter(NullLogger<StdinWriter>.Instance);
        var matcher = new PromptMatcher(realWriter, NullLogger<PromptMatcher>.Instance);

        var result = await matcher.WaitForPromptAndRespondAsync(
            stdoutReader, stdinStream, allowlist, FastPolicy());

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Timeout);
        result.Error.DetailCode.Should().Be("terminal.promptTimeout");
    }

    [Fact]
    public async Task WaitForPromptAndRespondAsync_UnmatchedPrompt_NoStdinWriteOccurs()
    {
        // Verify: for an unmatched non-credential prompt the mock IStdinWriter is never called.
        var mockWriter = new Mock<IStdinWriter>(MockBehavior.Strict);

        var allowlist = new List<PromptAllowlistEntry> { new("Enter username:", "user") };
        using var stdoutReader = new StringReader("Initialising...");
        using var stdinStream = new MemoryStream();
        var matcher = new PromptMatcher(mockWriter.Object, NullLogger<PromptMatcher>.Instance);

        await matcher.WaitForPromptAndRespondAsync(
            stdoutReader, stdinStream, allowlist, FastPolicy());

        // MockBehavior.Strict will throw if WriteAsync was called — the test passing proves it was not.
        mockWriter.VerifyNoOtherCalls();
    }

    // ── AC-4: Credential prompt not in allowlist → NO input sent ────────────

    [Theory]
    [InlineData("Enter password:")]
    [InlineData("Please enter your token:")]
    [InlineData("API key:")]
    [InlineData("Secret value:")]
    [InlineData("Enter passphrase:")]
    public async Task WaitForPromptAndRespondAsync_CredentialPromptNotInAllowlist_ReturnsPolicyDenied(
        string credentialLine)
    {
        // Empty allowlist — the credential prompt is not approved.
        var allowlist = new List<PromptAllowlistEntry>();
        using var stdoutReader = new StringReader(credentialLine);
        using var stdinStream = new MemoryStream();
        var realWriter = new StdinWriter(NullLogger<StdinWriter>.Instance);
        var matcher = new PromptMatcher(realWriter, NullLogger<PromptMatcher>.Instance);

        var result = await matcher.WaitForPromptAndRespondAsync(
            stdoutReader, stdinStream, allowlist, FastPolicy());

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
        result.Error.DetailCode.Should().Be("terminal.credentialPromptDenied");
    }

    [Fact]
    public async Task WaitForPromptAndRespondAsync_CredentialPromptNotInAllowlist_NoStdinWriteOccurs()
    {
        // Use a strict mock to prove WriteAsync is never called for credential prompts.
        var mockWriter = new Mock<IStdinWriter>(MockBehavior.Strict);

        var allowlist = new List<PromptAllowlistEntry>();
        using var stdoutReader = new StringReader("Enter password:");
        using var stdinStream = new MemoryStream();
        var matcher = new PromptMatcher(mockWriter.Object, NullLogger<PromptMatcher>.Instance);

        await matcher.WaitForPromptAndRespondAsync(
            stdoutReader, stdinStream, allowlist, FastPolicy());

        mockWriter.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WaitForPromptAndRespondAsync_CredentialPromptExplicitlyAllowlisted_IsAccepted()
    {
        // AC-4 exception: an operator may explicitly allowlist a credential prompt.
        // In that case the allowlist check wins (first evaluated) and the response is sent.
        var allowlist = new List<PromptAllowlistEntry>
        {
            new("Enter password:", "APPROVED_FIXED_VALUE")
        };

        using var stdoutReader = new StringReader("Enter password:");
        using var stdinStream = new MemoryStream();
        var realWriter = new StdinWriter(NullLogger<StdinWriter>.Instance);
        var matcher = new PromptMatcher(realWriter, NullLogger<PromptMatcher>.Instance);

        var result = await matcher.WaitForPromptAndRespondAsync(
            stdoutReader, stdinStream, allowlist, FastPolicy());

        // The allowlist match takes precedence over the credential detector.
        result.IsSuccess.Should().BeTrue();
    }

    // ── Security: approved response values must not appear in error messages ──

    [Fact]
    public async Task WaitForPromptAndRespondAsync_CredentialError_DoesNotLeakPromptContent()
    {
        var allowlist = new List<PromptAllowlistEntry>();
        using var stdoutReader = new StringReader("Enter password: hunter2");
        using var stdinStream = new MemoryStream();
        var realWriter = new StdinWriter(NullLogger<StdinWriter>.Instance);
        var matcher = new PromptMatcher(realWriter, NullLogger<PromptMatcher>.Instance);

        var result = await matcher.WaitForPromptAndRespondAsync(
            stdoutReader, stdinStream, allowlist, FastPolicy());

        // The stdout content should not be embedded verbatim in any error message.
        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().NotContain("hunter2");
    }

    // ── Null-guards ──────────────────────────────────────────────────────────

    [Fact]
    public async Task WaitForPromptAndRespondAsync_NullStdoutReader_ThrowsArgumentNullException()
    {
        var matcher = CreateMatcher();
        using var ms = new MemoryStream();
        Func<Task> act = () => matcher.WaitForPromptAndRespondAsync(
            null!, ms, [], FastPolicy());
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task WaitForPromptAndRespondAsync_NullStdinStream_ThrowsArgumentNullException()
    {
        var matcher = CreateMatcher();
        using var reader = new StringReader("prompt:");
        Func<Task> act = () => matcher.WaitForPromptAndRespondAsync(
            reader, null!, [], FastPolicy());
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task WaitForPromptAndRespondAsync_NullAllowlist_ThrowsArgumentNullException()
    {
        var matcher = CreateMatcher();
        using var reader = new StringReader("prompt:");
        using var ms = new MemoryStream();
        Func<Task> act = () => matcher.WaitForPromptAndRespondAsync(
            reader, ms, null!, FastPolicy());
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task WaitForPromptAndRespondAsync_NullPolicy_ThrowsArgumentNullException()
    {
        var matcher = CreateMatcher();
        using var reader = new StringReader("prompt:");
        using var ms = new MemoryStream();
        Func<Task> act = () => matcher.WaitForPromptAndRespondAsync(
            reader, ms, [], null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static PromptMatcher CreateMatcher() =>
        new(new StdinWriter(NullLogger<StdinWriter>.Instance),
            NullLogger<PromptMatcher>.Instance);
}
