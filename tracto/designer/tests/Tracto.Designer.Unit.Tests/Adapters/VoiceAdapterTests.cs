namespace Tracto.Designer.Unit.Tests.Adapters;

using FluentAssertions;
using Tracto.Designer.Infrastructure.Adapters;

public class VoiceAdapterTests
{
    [Fact]
    public void NullVoiceAdapter_IsAvailable_False()
    {
        new NullVoiceAdapter().IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task NullVoiceAdapter_StartAsync_DoesNotThrow()
    {
        await new NullVoiceAdapter().Invoking(a => a.StartAsync()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task NullVoiceAdapter_StopAsync_DoesNotThrow()
    {
        await new NullVoiceAdapter().Invoking(a => a.StopAsync()).Should().NotThrowAsync();
    }

    [Fact]
    public void NullVoiceAdapter_GetTranscriptions_IsEmpty()
    {
        using var cts = new System.Threading.CancellationTokenSource(100);
        new NullVoiceAdapter().GetTranscriptionsAsync(cts.Token).Should().NotBeNull();
    }

    [Fact]
    public void WindowsVoiceAdapter_IsAvailable_IsBoolean()
    {
        using var adapter = new WindowsVoiceAdapter();
        adapter.Invoking(a => { var _ = a.IsAvailable; }).Should().NotThrow();
    }
}
