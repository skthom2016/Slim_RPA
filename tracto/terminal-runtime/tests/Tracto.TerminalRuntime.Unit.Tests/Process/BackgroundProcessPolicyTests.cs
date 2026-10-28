using FluentAssertions;
using Tracto.TerminalRuntime.Domain.Process;

namespace Tracto.TerminalRuntime.Unit.Tests.Process;

/// <summary>
/// Unit tests for <see cref="BackgroundProcessPolicy"/>.
/// TER-US-015 AC-7: background-process patterns must be detected reliably.
/// </summary>
public sealed class BackgroundProcessPolicyTests
{
    // ── ContainsBackgroundPattern ─────────────────────────────────────────────

    [Fact]
    public void ContainsBackgroundPattern_BashTrailingAmpersand_ReturnsTrue()
    {
        var args = new[] { "-c", "echo hello & echo world" };
        BackgroundProcessPolicy.ContainsBackgroundPattern(args).Should().BeTrue();
    }

    [Fact]
    public void ContainsBackgroundPattern_StartJob_ReturnsTrue()
    {
        var args = new[] { "-Command", "Start-Job { Write-Host 'bg' }" };
        BackgroundProcessPolicy.ContainsBackgroundPattern(args).Should().BeTrue();
    }

    [Fact]
    public void ContainsBackgroundPattern_StartProcess_ReturnsTrue()
    {
        var args = new[] { "-Command", "Start-Process notepad" };
        BackgroundProcessPolicy.ContainsBackgroundPattern(args).Should().BeTrue();
    }

    [Fact]
    public void ContainsBackgroundPattern_Nohup_ReturnsTrue()
    {
        var args = new[] { "-c", "nohup ./server.sh" };
        BackgroundProcessPolicy.ContainsBackgroundPattern(args).Should().BeTrue();
    }

    [Fact]
    public void ContainsBackgroundPattern_Disown_ReturnsTrue()
    {
        var args = new[] { "-c", "sleep 100 & disown" };
        BackgroundProcessPolicy.ContainsBackgroundPattern(args).Should().BeTrue();
    }

    [Fact]
    public void ContainsBackgroundPattern_CleanCommand_ReturnsFalse()
    {
        var args = new[] { "-c", "echo hello" };
        BackgroundProcessPolicy.ContainsBackgroundPattern(args).Should().BeFalse();
    }

    [Fact]
    public void ContainsBackgroundPattern_EmptyList_ReturnsFalse()
    {
        BackgroundProcessPolicy.ContainsBackgroundPattern([]).Should().BeFalse();
    }

    [Fact]
    public void ContainsBackgroundPattern_LiteralAmpersandInUrl_ReturnsFalse()
    {
        // A URL parameter like ?a=1&b=2 should not be flagged since the marker is " &" (with leading space).
        var args = new[] { "-c", "curl 'http://example.com?a=1&b=2'" };
        BackgroundProcessPolicy.ContainsBackgroundPattern(args).Should().BeFalse();
    }

    // ── FindFirstMarker ───────────────────────────────────────────────────────

    [Fact]
    public void FindFirstMarker_DetectedPattern_ReturnsMarker()
    {
        var args = new[] { "-c", "sleep 10 & echo done" };
        string? marker = BackgroundProcessPolicy.FindFirstMarker(args);
        marker.Should().NotBeNull();
        marker.Should().Be("&");
    }

    [Fact]
    public void FindFirstMarker_NoPattern_ReturnsNull()
    {
        var args = new[] { "-c", "ls -la" };
        BackgroundProcessPolicy.FindFirstMarker(args).Should().BeNull();
    }
}
