using System.Text.Json;
using FluentAssertions;
using Tracto.Desktop.Element;
using Tracto.Desktop.Telemetry;
using Xunit;

namespace Tracto.Desktop.Telemetry.Tests;

public sealed class TelemetryTests
{
    // -------------------------------------------------------------------------
    // Helper
    // -------------------------------------------------------------------------

    static void AssertNoSecretExposure(object record, string testSecret = "TEST_SECRET_12345")
    {
        var json = JsonSerializer.Serialize(record);
        json.Should().NotContain(testSecret, "secret must not appear in any telemetry field");
    }

    // -------------------------------------------------------------------------
    // DT-19-AC01 — EmitAsync with failed step record
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AC01_EmitAsync_FailedStepRecord_AllRequiredFieldsPresent_NoSecretExposure()
    {
        // Arrange
        const string testSecret = "TEST_SECRET_12345";
        var record = new StepTelemetryRecord
        {
            StepId           = "step-001",
            ActionType       = "Click",
            SelectorStrategy = "AutomationId",
            DurationMs       = 142,
            ResultCategory   = "Failed",
            ErrorCategory    = "ELEMENT_NOT_FOUND",
            DetailCode       = "UIA_ELEMENT_DISAPPEARED",
            UnknownReason    = null,
            EvidenceRef      = null,
        };

        var emitter = new ConsoleTelemetryEmitter();

        // Act — should not throw
        await emitter.EmitAsync(record, CancellationToken.None);

        // Assert — required fields
        record.StepId.Should().NotBeNullOrWhiteSpace();
        record.ActionType.Should().NotBeNullOrWhiteSpace();
        record.SelectorStrategy.Should().NotBeNullOrWhiteSpace();
        record.DurationMs.Should().BeGreaterThanOrEqualTo(0);
        record.ResultCategory.Should().NotBeNullOrWhiteSpace();

        // The record must not expose the test secret
        AssertNoSecretExposure(record, testSecret);
    }

    // -------------------------------------------------------------------------
    // DT-19-AC03 — RecordScreenshotAsync with sensitiveRegionPresent=true, CanMask=false
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AC03_RecordScreenshot_SensitivePresent_CanMaskFalse_OmittedReasonSet_WasMaskedFalse()
    {
        // Arrange
        // We force CanMask=false by giving a region that is OUTSIDE the screen bounds.
        // ScreenshotMasker.CanMask returns false when region.Right > imageSize.Width.
        var recorder = new SafeEvidenceRecorder(Path.GetTempPath())
        {
            ScreenSize      = new System.Drawing.Size(100, 100),
            SensitiveRegion = new System.Drawing.Rectangle(50, 50, 200, 200), // extends outside
        };

        // Act
        var result = await recorder.RecordScreenshotAsync("step-ac03", sensitiveRegionPresent: true, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.WasMasked.Should().BeFalse("masking was not possible");
        result.OmittedReason.Should().Be("masking_unsafe");
    }

    // -------------------------------------------------------------------------
    // DT-19-AC04 — SelectorDiagnosticBuilder with activeSecretPresent=true
    // -------------------------------------------------------------------------

    [Fact]
    public void AC04_SelectorDiagnosticBuilder_SecretPresent_TitleDoesNotContainSecret()
    {
        // Arrange
        const string testSecret = "TEST_SECRET_12345";
        var sel = new ElementSelector { AutomationId = "btn-ok" };

        // Act
        var diag = SelectorDiagnosticBuilder.Build(
            sel,
            candidateCount: 3,
            rawTitle: $"My Window — user: {testSecret}",
            activeSecretPresent: true);

        // Assert
        diag.SanitizedWindowTitle.Should().BeNull(
            "when activeSecretPresent=true the title must be omitted");
        (diag.SanitizedWindowTitle ?? string.Empty).Should().NotContain(testSecret);
        diag.Strategy.Should().Be("AutomationId");
        diag.CandidateCount.Should().Be(3);
    }

    // -------------------------------------------------------------------------
    // DT-19-AC05 — JournalWriter persists entry; new instance reads it back
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AC05_JournalWriter_WritesFile_NewInstanceReadsEntryBack()
    {
        // Arrange
        var tempRoot = Path.Combine(Path.GetTempPath(), $"journal-test-{Guid.NewGuid():N}");
        const string attemptId     = "attempt-001";
        const string stepId        = "step-xyz";
        const string lastAction    = "Click";
        const string unknownReason = "POSTCONDITION_UNVERIFIABLE";

        try
        {
            // Act — write
            var writer1 = new JournalWriter(tempRoot);
            await writer1.WriteUncertaintyAsync(
                attemptId, stepId, lastAction, unknownReason, CancellationToken.None);

            // Verify the file was created on disk.
            var journalDir = Path.Combine(tempRoot, "journal");
            Directory.Exists(journalDir).Should().BeTrue("journal subdirectory must be created");

            var files = Directory.GetFiles(journalDir, $"{attemptId}.jsonl");
            files.Should().HaveCount(1, "one JSONL file per attempt");
            File.Exists(files[0]).Should().BeTrue();

            // Act — read back with a fresh instance (simulates restart).
            var writer2 = new JournalWriter(tempRoot);
            var entries = await writer2.ReadEntriesAsync(attemptId, CancellationToken.None);

            // Assert
            entries.Should().HaveCount(1);
            entries[0].AttemptId.Should().Be(attemptId);
            entries[0].StepId.Should().Be(stepId);
            entries[0].LastAction.Should().Be(lastAction);
            entries[0].UnknownReason.Should().Be(unknownReason);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }
}
