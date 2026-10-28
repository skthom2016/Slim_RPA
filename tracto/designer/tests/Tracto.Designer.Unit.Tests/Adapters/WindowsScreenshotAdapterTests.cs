namespace Tracto.Designer.Unit.Tests.Adapters;

using FluentAssertions;
using Tracto.Designer.Infrastructure.Adapters;

public class WindowsScreenshotAdapterTests
{
    [Fact]
    public void IsAvailable_Returns_True()
    {
        new WindowsScreenshotAdapter().IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void NullScreenshotAdapter_IsAvailable_Returns_False()
    {
        new NullScreenshotAdapter().IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task CaptureAsync_Returns_NonNull_Or_Null_Without_Throwing()
    {
        var adapter = new WindowsScreenshotAdapter();
        // This test runs headless — capture may return null (no display). That is valid per AC-05.
        var act = async () => await adapter.CaptureAsync("test-session", 1);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NullAdapter_CaptureAsync_Returns_Null()
    {
        var result = await new NullScreenshotAdapter().CaptureAsync("x", 1);
        result.Should().BeNull();
    }
}
