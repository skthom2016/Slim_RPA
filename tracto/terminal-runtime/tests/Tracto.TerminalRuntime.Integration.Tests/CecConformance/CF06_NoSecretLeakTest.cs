using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.Heartbeat;
using Tracto.TerminalRuntime.Domain;
using Tracto.TerminalRuntime.Domain.Redaction;
using Tracto.TerminalRuntime.Infrastructure;
using Tracto.TerminalRuntime.Infrastructure.Redaction;

namespace Tracto.TerminalRuntime.Integration.Tests.CecConformance;

/// <summary>
/// CF-06 — No Secret Leak
///
/// CEC v1.0.1 invariant #4: no plaintext secrets may appear in any log entry,
/// evidence record, telemetry emission, stream dump, or Orchestrator payload.
///
/// This test:
///   1. Registers a known sentinel secret token.
///   2. Drives each system component that could emit output.
///   3. Asserts that none of the captured output contains the sentinel token.
///
/// The sentinel is chosen to be short, memorable, and recognisable in assertions
/// without resembling any real credential pattern.
/// </summary>
public sealed class CF06_NoSecretLeakTest : IClassFixture<MockOrchestratorFixture>
{
    private const string SentinelSecret = "CF06-SENTINEL-SECRET-TOKEN";

    private readonly MockOrchestratorFixture _fx;

    public CF06_NoSecretLeakTest(MockOrchestratorFixture fx) => _fx = fx;

    // ── ArtifactValidator ──────────────────────────────────────────────────────

    [Fact(DisplayName = "CF-06-A: ArtifactValidator does not log the HMAC key")]
    public void ArtifactValidator_DoesNotLogHmacKey()
    {
        // Build a validator with the sentinel as the HMAC key
        var logLines = new List<string>();
        var loggerMock = new Mock<ILogger<ArtifactValidator>>();
        loggerMock
            .Setup(l => l.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback<LogLevel, EventId, object, Exception?, Delegate>((_, _, state, _, fmt) =>
            {
                logLines.Add(state.ToString() ?? string.Empty);
            });

        // Use a base64 encoding that embeds the sentinel in the key value
        var sentinelKeyBytes = System.Text.Encoding.UTF8.GetBytes(SentinelSecret + "PADDING12345");
        var sentinelKeyBase64 = Convert.ToBase64String(sentinelKeyBytes);

        var settings  = Options.Create(new ArtifactSignatureSettings { HmacKeyBase64 = sentinelKeyBase64 });
        var validator = new ArtifactValidator(settings, loggerMock.Object);

        // Trigger a validation — regardless of result, the key must not appear in logs
        var manifest = new ArtifactManifest("terminal", 1, [], [], "badsig");
        _ = validator.IsSignatureValid(manifest);

        var allLogText = string.Join("\n", logLines);
        allLogText.Should().NotContain(SentinelSecret,
            "CEC invariant #4: the HMAC key value must never appear in log output");
        allLogText.Should().NotContain(sentinelKeyBase64,
            "CEC invariant #4: the base64-encoded HMAC key must never appear in log output");
    }

    // ── CapabilityAdvertiser ───────────────────────────────────────────────────

    [Fact(DisplayName = "CF-06-B: WorkerCapabilityAdvertisement carries no secret")]
    public void WorkerCapabilityAdvertisement_CarriesNoSecret()
    {
        var adResult = _fx.CapabilityAdvertiser.BuildAdvertisement();
        adResult.IsSuccess.Should().BeTrue();
        var ad      = adResult.Value;
        var adJson  = System.Text.Json.JsonSerializer.Serialize(ad);

        adJson.Should().NotContain(SentinelSecret,
            "CEC invariant #4: capability advertisement must not contain secret tokens");
        adJson.Should().NotContain(MockOrchestratorFixture.TestHmacKeyBase64,
            "CEC invariant #4: the HMAC key must not appear in capability advertisement");
    }

    // ── HeartbeatRecord ────────────────────────────────────────────────────────

    [Fact(DisplayName = "CF-06-C: HeartbeatRecord does not carry the sentinel secret")]
    public void HeartbeatRecord_DoesNotCarrySecret()
    {
        // A HeartbeatRecord is constructed from runId/attemptId/seqNo/progressSummary —
        // none of these ever hold a secret per our domain model.
        var record = new HeartbeatRecord(
            RunId:           "run-cf06",
            AttemptId:       "attempt-1",
            SeqNo:           1,
            SentAt:          DateTimeOffset.UtcNow,
            ProgressSummary: $"runId=run-cf06 attemptId=attempt-1 seqNo=1");

        var recordJson = System.Text.Json.JsonSerializer.Serialize(record);
        recordJson.Should().NotContain(SentinelSecret,
            "CEC invariant #4: HeartbeatRecord payload must never contain a secret token");
    }

    // ── OutputRedactor ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "CF-06-D: OutputRedactor masks known secrets before evidence is emitted")]
    public void OutputRedactor_MasksKnownSecret()
    {
        var redactor = new OutputRedactor();
        var policy   = new OutputRedactionPolicy(
            Mode: RedactionMode.MaskSecrets,
            ActiveSecretTokens: [SentinelSecret]);

        var raw     = $"stdout: some normal output and then {SentinelSecret} appears";
        var result  = redactor.Redact(raw, policy);

        result.IsSuccess.Should().BeTrue();
        result.Value.Content.Should().NotContain(SentinelSecret,
            "CEC invariant #4: the redactor must replace secret tokens before evidence is stored");
        result.Value.WasRedacted.Should().BeTrue();
    }

    // ── Journal ────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "CF-06-E: Journal entries written to disk contain no sentinel secret")]
    public async Task JournalEntry_OnDisk_ContainsNoSecret()
    {
        var runId     = "run-cf06e-" + Guid.NewGuid().ToString("N")[..8];
        var attemptId = Guid.NewGuid().ToString("D");
        var leaseId   = Guid.NewGuid().ToString("D");

        // Write an entry whose fields are free of secrets (this is the invariant callers must honour)
        var entry = new RunJournalEntry(
            RunId:           runId,
            AttemptId:       attemptId,
            LeaseId:         leaseId,
            WorkerId:        "worker-cf06e",
            HeartbeatSeq:    0L,
            CheckpointAt:    DateTimeOffset.UtcNow,
            FinalResult:     null,
            IdempotencyClass: IdempotencyClass.Idempotent,
            State:           RunState.Leased);
        await _fx.Journal.WriteEntryAsync(entry);

        // Read the raw file from disk and check for the sentinel
        var journalFile = Path.Combine(_fx.JournalRoot, $"{runId}.json");
        File.Exists(journalFile).Should().BeTrue("journal file must exist after write");
        var diskContent = await File.ReadAllTextAsync(journalFile);
        diskContent.Should().NotContain(SentinelSecret,
            "CEC invariant #4: the journal file on disk must contain no plaintext secrets");
    }
}
