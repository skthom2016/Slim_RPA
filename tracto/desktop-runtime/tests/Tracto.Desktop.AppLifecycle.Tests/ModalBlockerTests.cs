using FluentAssertions;
using Tracto.Desktop.AppLifecycle;
using Xunit;

namespace Tracto.Desktop.AppLifecycle.Tests;

public sealed class ModalBlockerTests
{
    private static readonly IntPtr TargetHwnd = new(0x1000);

    // ── DT-03-AC06: Unexpected window above target → IsBlocked=true ──────────

    [Fact]
    public void CheckForUnexpectedModal_WithUnexpectedWindowAbove_ReturnsIsBlockedTrue()
    {
        // Arrange: enumerator says a modal is present
        var enumerator = new MockWindowEnumerator
        {
            HasUnexpectedModalOverride = true,
            TopLevelWindows =
            [
                new WindowInfo(new IntPtr(0x2000), "Security Alert", "DialogClass", ProcessId: 99)
            ]
        };
        var blocker = new ModalBlocker(enumerator);

        // Act
        var result = blocker.CheckForUnexpectedModal(TargetHwnd);

        // Assert
        result.IsBlocked.Should().BeTrue();
        result.ModalTitle.Should().Be("Security Alert");
        result.Evidence.Should().NotBeNullOrEmpty();
        result.Evidence.Should().Contain("withheld");
    }

    // ── DT-03-AC04/AC05: No unexpected window → IsBlocked=false ─────────────

    [Fact]
    public void CheckForUnexpectedModal_WithNoUnexpectedWindow_ReturnsIsBlockedFalse()
    {
        // Arrange: enumerator says no modal
        var enumerator = new MockWindowEnumerator
        {
            HasUnexpectedModalOverride = false,
            TopLevelWindows = []
        };
        var blocker = new ModalBlocker(enumerator);

        // Act
        var result = blocker.CheckForUnexpectedModal(TargetHwnd);

        // Assert
        result.IsBlocked.Should().BeFalse();
        result.ModalTitle.Should().BeNull();
        result.Evidence.Should().NotBeNullOrEmpty();
    }

    // ── ModalBlockResult properties are preserved ─────────────────────────────

    [Fact]
    public void ModalBlockResult_Properties_ArePreserved()
    {
        var r = new ModalBlockResult(IsBlocked: true, ModalTitle: "Alert", Evidence: "evidence text");

        r.IsBlocked.Should().BeTrue();
        r.ModalTitle.Should().Be("Alert");
        r.Evidence.Should().Be("evidence text");
    }

    // ── Long title is truncated in evidence (sanitization) ───────────────────

    [Fact]
    public void CheckForUnexpectedModal_LongModalTitle_IsTruncatedInEvidence()
    {
        var longTitle = new string('A', 200);
        var enumerator = new MockWindowEnumerator
        {
            HasUnexpectedModalOverride = true,
            TopLevelWindows =
            [
                new WindowInfo(new IntPtr(0x3000), longTitle, "SomeClass", ProcessId: 77)
            ]
        };
        var blocker = new ModalBlocker(enumerator);

        var result = blocker.CheckForUnexpectedModal(TargetHwnd);

        result.IsBlocked.Should().BeTrue();
        result.ModalTitle.Should().NotBeNull();
        result.ModalTitle!.Length.Should().BeLessOrEqualTo(130); // 128 chars + ellipsis
    }

    // ── WindowInfo record equality ────────────────────────────────────────────

    [Fact]
    public void WindowInfo_RecordEquality_WorksCorrectly()
    {
        var a = new WindowInfo(new IntPtr(0x100), "Title", "Class", 42);
        var b = new WindowInfo(new IntPtr(0x100), "Title", "Class", 42);
        var c = new WindowInfo(new IntPtr(0x200), "Title", "Class", 42);

        a.Should().Be(b);
        a.Should().NotBe(c);
    }
}
