namespace Tracto.Designer.Unit.Tests.Privacy;

using FluentAssertions;
using Tracto.Designer.Core.Privacy;
using Tracto.Designer.Core.Recording;
using Tracto.Designer.Core.Scripting;

public class PrivacyControlTests
{
    private static PrivacySettings DefaultSettings() => new();

    private static RawEvent MakeEvent(bool isPassword = false, string? value = null) =>
        new(1, DateTimeOffset.UtcNow, RawEventType.TextInput, "Notepad", "Win",
            null, null, null, value, isPassword, null, 0, 0);

    private static TranscriptEntry MakeEntry(bool isPassword = false, string? narration = null) =>
        new(1, DateTimeOffset.UtcNow, "Step desc", MakeEvent(isPassword), narration, null);

    [Fact]
    public void SensitivePatternDetector_Scrubs_Email()
    {
        var result = SensitivePatternDetector.Scrub("Contact me at user@example.com please", PrivacyPolicy.Default);
        result.Should().Contain("[REDACTED]");
        result.Should().NotContain("user@example.com");
    }

    [Fact]
    public void SensitivePatternDetector_Does_Not_Scrub_When_Not_In_Policy()
    {
        var result = SensitivePatternDetector.Scrub("user@example.com", PrivacyPolicy.None);
        result.Should().Contain("user@example.com");
    }

    [Fact]
    public void SensitivePatternDetector_Scrubs_SSN()
    {
        var result = SensitivePatternDetector.Scrub("SSN is 123-45-6789", PrivacyPolicy.RedactSsn);
        result.Should().Contain("[REDACTED]");
    }

    [Fact]
    public void PrivacyFilter_Redacts_Narration_PII()
    {
        var settings = DefaultSettings();
        settings.Policy = PrivacyPolicy.Default | PrivacyPolicy.RedactEmail;
        var filter = new PrivacyFilter(settings);
        var entries = new[] { MakeEntry(narration: "Send to admin@corp.com") };
        var filtered = filter.FilterTranscript(entries).ToList();
        filtered[0].NarrationText.Should().NotContain("admin@corp.com");
        filtered[0].NarrationText.Should().Contain("[REDACTED]");
    }

    [Fact]
    public void PrivacyFilter_Script_Scrubs_Parameter_Values()
    {
        var settings = DefaultSettings();
        var filter = new PrivacyFilter(settings);
        var s = new Script { Id = "t", Name = "T", Version = "1.0.0", TargetRuntime = "desktop" };
        var step = new ScriptStep { StepNumber = 1, Description = "Email user@test.com", Action = "type" };
        step.Parameters["to"] = "user@test.com";
        s.Steps.Add(step);
        var filtered = filter.FilterScript(s);
        filtered.Steps[0].Parameters["to"].Should().Be("[REDACTED]");
    }

    [Fact]
    public void PrivacyPolicy_Default_Has_Passwords_And_Email()
    {
        (PrivacyPolicy.Default & PrivacyPolicy.RedactPasswords).Should().NotBe(PrivacyPolicy.None);
        (PrivacyPolicy.Default & PrivacyPolicy.RedactEmail).Should().NotBe(PrivacyPolicy.None);
    }

    [Fact]
    public void PrivacySettings_Reset_Restores_Defaults()
    {
        var s = DefaultSettings();
        s.Policy = PrivacyPolicy.None;
        s.Reset();
        s.Policy.Should().Be(PrivacyPolicy.Default);
    }

    [Fact]
    public void PrivacyFilter_Null_Narration_Unchanged()
    {
        var filter = new PrivacyFilter(DefaultSettings());
        var entries = new[] { MakeEntry(narration: null) };
        var filtered = filter.FilterTranscript(entries).ToList();
        filtered[0].NarrationText.Should().BeNull();
    }

    [Fact]
    public void PrivacyFilter_Password_Field_Value_Is_Redacted_In_SourceEvent()
    {
        // DS-14: The primary privacy guarantee — password-field values must not
        // flow through FilterTranscript unredacted.
        var settings = DefaultSettings();  // Default includes RedactPasswords
        var filter = new PrivacyFilter(settings);

        var entry = new TranscriptEntry(
            1,
            DateTimeOffset.UtcNow,
            "Type password",
            MakeEvent(isPassword: true, value: "S3cr3tP@ssw0rd"),
            null,
            null);

        var filtered = filter.FilterTranscript(new[] { entry }).ToList();

        filtered[0].SourceEvent.Value.Should().Be(SensitivePatternDetector.Redacted,
            "password-field input value must be replaced with the redacted marker");
        filtered[0].SourceEvent.Value.Should().NotBe("S3cr3tP@ssw0rd",
            "the original plaintext password must not survive the filter");
    }

    [Fact]
    public void PrivacyFilter_Non_Password_Field_Value_Is_Not_Redacted()
    {
        var settings = DefaultSettings();
        var filter = new PrivacyFilter(settings);

        var entry = new TranscriptEntry(
            1,
            DateTimeOffset.UtcNow,
            "Type username",
            MakeEvent(isPassword: false, value: "john.doe"),
            null,
            null);

        var filtered = filter.FilterTranscript(new[] { entry }).ToList();
        filtered[0].SourceEvent.Value.Should().Be("john.doe",
            "non-password fields must pass through unchanged");
    }

    [Fact]
    public void PrivacySettingsViewModel_RedactPasswords_Cannot_Be_Set_To_False()
    {
        // DS-14: RedactPasswords is always enabled.
        var settings = DefaultSettings();
        var vm = new Tracto.Designer.App.ViewModels.PrivacySettingsViewModel(settings);

        vm.RedactPasswords.Should().BeTrue("default policy includes RedactPasswords");

        // Attempt to disable — must be silently rejected
        vm.RedactPasswords = false;
        vm.RedactPasswords.Should().BeTrue("the always-on constraint must prevent disabling");
    }
}
