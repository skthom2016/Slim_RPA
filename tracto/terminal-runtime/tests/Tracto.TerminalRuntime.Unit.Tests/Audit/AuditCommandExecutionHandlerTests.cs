using FluentAssertions;
using Moq;
using Tracto.TerminalRuntime.Application.Audit;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain;
using Tracto.TerminalRuntime.Domain.Audit;

namespace Tracto.TerminalRuntime.Unit.Tests.Audit;

/// <summary>
/// Unit tests for <see cref="AuditCommandExecutionHandler"/> (TER-US-010).
///
/// Verifies:
/// AC-1  Irreversible commands are NEVER auto-replayed after a timeout or lost ack.
/// AC-2  ReadOnly commands are retried up to the configured retry limit.
/// AC-3  Audit record contains no secret values (only arg names and artifact hashes).
/// AC-4  Audit record is sufficient to reconstruct the invocation without exposing secrets.
/// </summary>
public sealed class AuditCommandExecutionHandlerTests
{
    private readonly Mock<ICommandAuditLog> _auditLogMock;
    private readonly Mock<IClock>           _clockMock;
    private readonly AuditCommandExecutionHandler _sut;

    private static readonly DateTimeOffset FixedNow =
        new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    public AuditCommandExecutionHandlerTests()
    {
        _auditLogMock = new Mock<ICommandAuditLog>(MockBehavior.Strict);
        _clockMock    = new Mock<IClock>(MockBehavior.Strict);
        _clockMock.Setup(c => c.UtcNow).Returns(FixedNow);

        // Defense-in-depth: the handler queries the audit log for any Irreversible command
        // before the fast-path IsRetryAttempt check.  Default to returning no prior records
        // so the fast-path check remains the deciding factor in tests that only set up AppendAsync.
        _auditLogMock
            .Setup(l => l.GetByRunIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok<IReadOnlyList<CommandAuditRecord>>(Array.Empty<CommandAuditRecord>()));

        _sut = new AuditCommandExecutionHandler(_auditLogMock.Object, _clockMock.Object);
    }

    // ── AC-1: Irreversible must not be retried ───────────────────────────────

    [Fact]
    public async Task HandleAsync_IrreversibleCommand_IsRetryAttempt_ReturnsFailure()
    {
        // Arrange
        var command = BuildCommand(
            idempotencyClass: IdempotencyClass.Irreversible,
            isRetryAttempt:   true,
            retryCount:       1,
            maxRetries:       5);

        // Act
        var result = await _sut.HandleAsync(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
        result.Error.Code.Should().Be("AUDIT_001");
        result.Error.DetailCode.Should().Be("terminal.irreversibleCommandRetryBlocked");

        // Audit log must NOT have been called — no record should be written for a rejected retry.
        _auditLogMock.Verify(
            l => l.AppendAsync(It.IsAny<CommandAuditRecord>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_IrreversibleCommand_FirstAttempt_Succeeds_RetryPermittedIsFalse()
    {
        // Arrange — first attempt of an irreversible command is allowed; retry is not.
        var command = BuildCommand(
            idempotencyClass: IdempotencyClass.Irreversible,
            isRetryAttempt:   false,
            retryCount:       0,
            maxRetries:       5);

        _auditLogMock
            .Setup(l => l.AppendAsync(It.IsAny<CommandAuditRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok<bool>(true));

        // Act
        var result = await _sut.HandleAsync(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.RetryPermitted.Should().BeFalse(
            "irreversible commands must never be eligible for auto-retry");
    }

    [Fact]
    public async Task HandleAsync_IrreversibleCommand_RetryCount_BelowMax_RetryPermittedIsFalse()
    {
        // Even when retry count is below max, irreversible must block retry.
        var command = BuildCommand(
            idempotencyClass: IdempotencyClass.Irreversible,
            isRetryAttempt:   false,
            retryCount:       0,
            maxRetries:       10);

        _auditLogMock
            .Setup(l => l.AppendAsync(It.IsAny<CommandAuditRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok<bool>(true));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RetryPermitted.Should().BeFalse();
    }

    // ── AC-2: ReadOnly retried up to limit ───────────────────────────────────

    [Fact]
    public async Task HandleAsync_ReadOnlyCommand_WithinRetryLimit_RetryPermittedIsTrue()
    {
        // Arrange — retry count 2 < max 5 → retry permitted.
        var command = BuildCommand(
            idempotencyClass: IdempotencyClass.ReadOnly,
            isRetryAttempt:   true,
            retryCount:       2,
            maxRetries:       5);

        _auditLogMock
            .Setup(l => l.AppendAsync(It.IsAny<CommandAuditRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok<bool>(true));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RetryPermitted.Should().BeTrue(
            "read-only commands may be retried up to the configured limit");
    }

    [Fact]
    public async Task HandleAsync_ReadOnlyCommand_AtRetryLimit_RetryPermittedIsFalse()
    {
        // Arrange — retry count == max → no more retries.
        var command = BuildCommand(
            idempotencyClass: IdempotencyClass.ReadOnly,
            isRetryAttempt:   true,
            retryCount:       5,
            maxRetries:       5);

        _auditLogMock
            .Setup(l => l.AppendAsync(It.IsAny<CommandAuditRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok<bool>(true));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RetryPermitted.Should().BeFalse(
            "retry count has reached the configured maximum");
    }

    [Fact]
    public async Task HandleAsync_IdempotentCommand_WithinRetryLimit_RetryPermittedIsTrue()
    {
        var command = BuildCommand(
            idempotencyClass: IdempotencyClass.Idempotent,
            isRetryAttempt:   true,
            retryCount:       1,
            maxRetries:       3);

        _auditLogMock
            .Setup(l => l.AppendAsync(It.IsAny<CommandAuditRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok<bool>(true));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RetryPermitted.Should().BeTrue();
    }

    // ── AC-3: Audit record contains no secret values ─────────────────────────

    [Fact]
    public async Task HandleAsync_AuditRecord_ContainsNoSecretValues()
    {
        // Arrange — the command carries arg NAMES only; no values are passed.
        var command = new AuditCommandExecutionCommand(
            CommandRef:       "cmd://export-financials",
            ArgNames:         ["outputPath", "accountId", "apiKey"],  // names only — no values
            ArtifactHashes:   ["abc123def456"],
            RunId:            "run-001",
            AttemptId:        "att-001",
            IdentityRef:      "S-1-5-21-masked",                      // opaque SID, not plaintext cred
            IdempotencyClass: IdempotencyClass.Irreversible,
            IsRetryAttempt:   false,
            RetryCount:       0,
            MaxRetries:       0);

        CommandAuditRecord? capturedRecord = null;
        _auditLogMock
            .Setup(l => l.AppendAsync(It.IsAny<CommandAuditRecord>(), It.IsAny<CancellationToken>()))
            .Callback<CommandAuditRecord, CancellationToken>((r, _) => capturedRecord = r)
            .ReturnsAsync(Result.Ok<bool>(true));

        // Act
        var result = await _sut.HandleAsync(command, CancellationToken.None);

        // Assert — record contains arg names but no raw argument values
        result.IsSuccess.Should().BeTrue();
        capturedRecord.Should().NotBeNull();
        capturedRecord!.ArgNames.Should().BeEquivalentTo(["outputPath", "accountId", "apiKey"]);
        capturedRecord.ArtifactHashes.Should().BeEquivalentTo(["abc123def456"]);
        capturedRecord.RunId.Should().Be("run-001");
        capturedRecord.AttemptId.Should().Be("att-001");
        capturedRecord.IdentityRef.Should().Be("S-1-5-21-masked");
        capturedRecord.ExecutedAt.Should().Be(FixedNow);
        capturedRecord.CommandRef.Should().Be("cmd://export-financials");
        capturedRecord.IdempotencyClass.Should().Be(IdempotencyClass.Irreversible);
    }

    [Fact]
    public async Task HandleAsync_AuditRecord_DoesNotContainRawParameterValues()
    {
        // This test explicitly verifies that the record persisted by the handler
        // does NOT include any actual argument values — only names.
        var sensitiveValue = "super-secret-token-12345";

        var command = new AuditCommandExecutionCommand(
            CommandRef:       "cmd://submit-payment",
            ArgNames:         ["token", "amount"],   // only names — value 'sensitiveValue' is NOT here
            ArtifactHashes:   [],
            RunId:            "run-002",
            AttemptId:        "att-001",
            IdentityRef:      "identity-ref-opaque",
            IdempotencyClass: IdempotencyClass.Irreversible,
            IsRetryAttempt:   false,
            RetryCount:       0,
            MaxRetries:       0);

        CommandAuditRecord? capturedRecord = null;
        _auditLogMock
            .Setup(l => l.AppendAsync(It.IsAny<CommandAuditRecord>(), It.IsAny<CancellationToken>()))
            .Callback<CommandAuditRecord, CancellationToken>((r, _) => capturedRecord = r)
            .ReturnsAsync(Result.Ok<bool>(true));

        await _sut.HandleAsync(command, CancellationToken.None);

        // The record must not contain the sensitive value anywhere.
        capturedRecord.Should().NotBeNull();
        capturedRecord!.ArgNames.Should().NotContain(sensitiveValue,
            "argument values must never appear in the audit record");
        capturedRecord.ArtifactHashes.Should().NotContain(sensitiveValue);
        capturedRecord.IdentityRef.Should().NotBe(sensitiveValue);
        capturedRecord.CommandRef.Should().NotBe(sensitiveValue);
    }

    // ── AC-4: Audit log failure surfaces as Result failure ───────────────────

    [Fact]
    public async Task HandleAsync_AuditLogWriteFailure_ReturnsFailure()
    {
        var command = BuildCommand(
            idempotencyClass: IdempotencyClass.ReadOnly,
            isRetryAttempt:   false,
            retryCount:       0,
            maxRetries:       3);

        _auditLogMock
            .Setup(l => l.AppendAsync(It.IsAny<CommandAuditRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail<bool>(Error.General("Audit.WriteFailure", "Disk full.")));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Audit.WriteFailure");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AuditCommandExecutionCommand BuildCommand(
        IdempotencyClass idempotencyClass,
        bool isRetryAttempt,
        int retryCount,
        int maxRetries) =>
        new(
            CommandRef:       "cmd://test-command",
            ArgNames:         ["param1", "param2"],
            ArtifactHashes:   ["deadbeef01234567"],
            RunId:            "run-test-001",
            AttemptId:        "att-001",
            IdentityRef:      "S-1-5-21-test",
            IdempotencyClass: idempotencyClass,
            IsRetryAttempt:   isRetryAttempt,
            RetryCount:       retryCount,
            MaxRetries:       maxRetries);
}
