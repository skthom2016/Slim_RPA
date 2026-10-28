using FluentAssertions;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Viewport;

namespace TractoRuntime.Browser.Tests.Viewport;

/// <summary>
/// Tests for BRW-US-006 AC-2 / BRW-007:
/// Supported viewports pass; unsupported viewports are rejected with a structured error
/// before any browser interaction.
/// </summary>
public sealed class ViewportValidatorTests
{
    private readonly ViewportValidator _sut = new();

    // -----------------------------------------------------------------------
    // AC: Certified viewports pass validation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_DefaultViewport_Passes()
    {
        // Default certified 1280×720 @ 1x must always pass
        await _sut.Invoking(v => v.ValidateAsync(ViewportSettings.Default, CancellationToken.None))
                  .Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(1280,  720, 1.0)]
    [InlineData(1366,  768, 1.0)]
    [InlineData(1440,  900, 1.0)]
    [InlineData(1920, 1080, 1.0)]
    [InlineData(1280,  800, 2.0)]
    [InlineData(1920, 1080, 2.0)]
    public async Task ValidateAsync_CertifiedViewport_Passes(int width, int height, double scale)
    {
        var viewport = new ViewportSettings { Width = width, Height = height, DeviceScaleFactor = scale };

        await _sut.Invoking(v => v.ValidateAsync(viewport, CancellationToken.None))
                  .Should().NotThrowAsync();
    }

    // -----------------------------------------------------------------------
    // AC: Unsupported viewport produces structured VALIDATION error
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData( 800,  600, 1.0)]   // classic unsupported resolution
    [InlineData(1024,  768, 1.0)]   // common but not certified
    [InlineData(1280,  720, 1.5)]   // wrong scale factor for a supported resolution
    [InlineData(1920, 1200, 1.0)]   // height mismatch
    public async Task ValidateAsync_UnsupportedViewport_ThrowsViewportNotSupportedException(
        int width, int height, double scale)
    {
        var viewport = new ViewportSettings { Width = width, Height = height, DeviceScaleFactor = scale };

        await _sut.Invoking(v => v.ValidateAsync(viewport, CancellationToken.None))
                  .Should().ThrowAsync<ViewportNotSupportedException>();
    }

    [Fact]
    public async Task ValidateAsync_UnsupportedViewport_ExceptionCarriesCorrectErrorCategory()
    {
        var unsupported = new ViewportSettings { Width = 800, Height = 600, DeviceScaleFactor = 1.0 };

        var ex = await Assert.ThrowsAsync<ViewportNotSupportedException>(
            () => _sut.ValidateAsync(unsupported, CancellationToken.None));

        ex.ErrorCategory.Should().Be(ErrorCategory.VALIDATION);
    }

    [Fact]
    public async Task ValidateAsync_UnsupportedViewport_ExceptionCarriesCorrectDetailCode()
    {
        var unsupported = new ViewportSettings { Width = 800, Height = 600, DeviceScaleFactor = 1.0 };

        var ex = await Assert.ThrowsAsync<ViewportNotSupportedException>(
            () => _sut.ValidateAsync(unsupported, CancellationToken.None));

        ex.DetailCode.Should().Be("browser.unsupportedViewport");
    }

    [Fact]
    public async Task ValidateAsync_UnsupportedViewport_ExceptionPreservesRequestedViewport()
    {
        var unsupported = new ViewportSettings { Width = 800, Height = 600, DeviceScaleFactor = 1.0 };

        var ex = await Assert.ThrowsAsync<ViewportNotSupportedException>(
            () => _sut.ValidateAsync(unsupported, CancellationToken.None));

        ex.RequestedViewport.Should().Be(unsupported);
    }

    [Fact]
    public async Task ValidateAsync_UnsupportedViewport_ExceptionListsCertifiedViewports()
    {
        var unsupported = new ViewportSettings { Width = 800, Height = 600, DeviceScaleFactor = 1.0 };

        var ex = await Assert.ThrowsAsync<ViewportNotSupportedException>(
            () => _sut.ValidateAsync(unsupported, CancellationToken.None));

        ex.CertifiedViewports.Should().NotBeEmpty();
    }

    // -----------------------------------------------------------------------
    // AC: Certified viewport list is non-empty and contains the default
    // -----------------------------------------------------------------------

    [Fact]
    public void GetCertifiedViewports_ReturnsNonEmptyList()
    {
        _sut.GetCertifiedViewports().Should().NotBeEmpty();
    }

    [Fact]
    public void GetCertifiedViewports_ContainsDefaultViewport()
    {
        _sut.GetCertifiedViewports()
            .Should().Contain(v =>
                v.Width == ViewportSettings.Default.Width &&
                v.Height == ViewportSettings.Default.Height &&
                Math.Abs(v.DeviceScaleFactor - ViewportSettings.Default.DeviceScaleFactor) < 0.001);
    }

    // -----------------------------------------------------------------------
    // Cancellation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await _sut.Invoking(v => v.ValidateAsync(ViewportSettings.Default, cts.Token))
                  .Should().ThrowAsync<OperationCanceledException>();
    }
}
