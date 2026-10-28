using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Stdin;
using Tracto.TerminalRuntime.Infrastructure.Stdin;

namespace Tracto.TerminalRuntime.Unit.Tests.Stdin;

/// <summary>
/// Unit tests for TER-US-012 AC-1 and AC-5 via <see cref="StdinWriter"/>.
/// All tests use in-memory streams — no real process is started.
/// </summary>
public sealed class StdinWriterTests
{
    private static StdinWriter CreateWriter() =>
        new(NullLogger<StdinWriter>.Instance);

    private static StdinPolicy DefaultPolicy() => new(MaxBytes: 65_536, PromptTimeoutMs: 5_000);

    // ── AC-1: Happy path — value written to stream ───────────────────────────

    [Fact]
    public async Task WriteAsync_ValidPayload_WritesValueToStream()
    {
        // Arrange
        var writer = CreateWriter();
        var policy = DefaultPolicy();
        using var ms = new MemoryStream();
        const string value = "approved_response";

        // Act
        var result = await writer.WriteAsync(ms, value, policy);

        // Assert
        result.IsSuccess.Should().BeTrue();
        ms.Position = 0;
        var written = new StreamReader(ms).ReadToEnd();
        written.Should().Be(value + "\n");
    }

    [Fact]
    public async Task WriteAsync_ValidPayload_ReturnsOkTrue()
    {
        var writer = CreateWriter();
        using var ms = new MemoryStream();

        var result = await writer.WriteAsync(ms, "hello", DefaultPolicy());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    // ── AC-5: Oversized input rejected before write ──────────────────────────

    [Fact]
    public async Task WriteAsync_PayloadExceedsMaxBytes_ReturnsValidationError()
    {
        // Arrange — build a payload that is exactly MaxBytes + 1 bytes (ASCII = 1 byte each)
        var policy = new StdinPolicy(MaxBytes: 100, PromptTimeoutMs: 5_000);
        var writer = CreateWriter();
        using var ms = new MemoryStream();
        string oversized = new('x', policy.MaxBytes + 1);   // 101 ASCII chars

        // Act
        var result = await writer.WriteAsync(ms, oversized, policy);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Validation);
        result.Error.DetailCode.Should().Be("terminal.stdinSizeExceeded");
    }

    [Fact]
    public async Task WriteAsync_OversizedPayload_StreamIsNotWrittenTo()
    {
        // Arrange — stream must remain empty if the write is rejected
        var policy = new StdinPolicy(MaxBytes: 10, PromptTimeoutMs: 5_000);
        var writer = CreateWriter();
        using var ms = new MemoryStream();
        string oversized = new('x', 11);

        // Act
        await writer.WriteAsync(ms, oversized, policy);

        // Assert — no bytes must reach the stream
        ms.Length.Should().Be(0);
    }

    [Fact]
    public async Task WriteAsync_ExactlyMaxBytes_IsAccepted()
    {
        // Boundary: exactly MaxBytes should succeed
        var policy = new StdinPolicy(MaxBytes: 50, PromptTimeoutMs: 5_000);
        var writer = CreateWriter();
        using var ms = new MemoryStream();
        string exact = new('a', 50);   // 50 UTF-8 bytes

        var result = await writer.WriteAsync(ms, exact, policy);

        result.IsSuccess.Should().BeTrue();
    }

    // ── AC-1: Closed pipe → ENVIRONMENT / terminal.processExited ────────────

    [Fact]
    public async Task WriteAsync_ClosedStream_ReturnsEnvironmentError()
    {
        var writer = CreateWriter();
        var ms = new MemoryStream();
        ms.Close();   // simulate closed pipe

        var result = await writer.WriteAsync(ms, "value", DefaultPolicy());

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Environment);
        result.Error.DetailCode.Should().Be("terminal.processExited");
    }

    // ── Security: written value must not appear in the error message ─────────

    [Fact]
    public async Task WriteAsync_ErrorMessage_DoesNotContainWrittenValue()
    {
        // A secret value must never be embedded in the returned error description.
        var policy = new StdinPolicy(MaxBytes: 5, PromptTimeoutMs: 5_000);
        var writer = CreateWriter();
        using var ms = new MemoryStream();
        const string secret = "p@ssw0rd!_secret";

        var result = await writer.WriteAsync(ms, secret, policy);

        // The result is a failure because the value exceeds MaxBytes=5.
        // The error message must not mention the value.
        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().NotContain(secret);
    }

    // ── Null-guards ──────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteAsync_NullStream_ThrowsArgumentNullException()
    {
        var writer = CreateWriter();
        Func<Task> act = () => writer.WriteAsync(null!, "value", DefaultPolicy());
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task WriteAsync_NullValue_ThrowsArgumentNullException()
    {
        var writer = CreateWriter();
        using var ms = new MemoryStream();
        Func<Task> act = () => writer.WriteAsync(ms, null!, DefaultPolicy());
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task WriteAsync_NullPolicy_ThrowsArgumentNullException()
    {
        var writer = CreateWriter();
        using var ms = new MemoryStream();
        Func<Task> act = () => writer.WriteAsync(ms, "value", null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
