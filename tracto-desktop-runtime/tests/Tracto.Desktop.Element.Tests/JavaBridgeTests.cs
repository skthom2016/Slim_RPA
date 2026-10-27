using FluentAssertions;
using Tracto.Desktop.Element;
using Xunit;

namespace Tracto.Desktop.Element.Tests;

/// <summary>
/// Unit tests for DT-06 Java Access Bridge adapter.
/// Uses MockJavaBridgeProvider so no live JRE or DLL is required.
/// </summary>
public sealed class JavaBridgeTests
{
    // -------------------------------------------------------------------------
    // Helper factory
    // -------------------------------------------------------------------------

    private static JavaBridgeSelector MakeSelector(string name = "OK", string? role = null)
        => new() { AccessibleName = name, Role = role };

    private static JavaElement MakeElement(string name = "OK", string role = "push button")
        => new()
        {
            WindowHandle = new IntPtr(42),
            Name = name,
            Role = role,
            X = 100, Y = 200, Width = 80, Height = 30
        };

    // -------------------------------------------------------------------------
    // AC: IsAvailable=false → FindAsync throws DesktopActionError
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FindAsync_WhenJabUnavailable_ThrowsDesktopActionError()
    {
        var provider = new MockJavaBridgeProvider { AvailableResult = false };
        var finder = new JavaBridgeElementFinder(provider);

        Func<Task> act = () => finder.FindAsync(MakeSelector(), IntPtr.Zero);

        var ex = await act.Should().ThrowAsync<DesktopActionError>();
        ex.Which.ErrorCode.Should().Be("TARGET_NOT_FOUND");
        ex.Which.Message.Should().Contain("Java Access Bridge not available");
    }

    [Fact]
    public async Task FindAsync_WhenJabUnavailable_ErrorCodeIsMachineReadable()
    {
        var provider = new MockJavaBridgeProvider { AvailableResult = false };
        var finder = new JavaBridgeElementFinder(provider);

        var ex = await Assert.ThrowsAsync<DesktopActionError>(
            () => finder.FindAsync(MakeSelector(), IntPtr.Zero));

        ex.ErrorCode.Should().Be("TARGET_NOT_FOUND");
    }

    // -------------------------------------------------------------------------
    // AC: Coordinate input attempt via JAB anchor alone → PolicyViolationException
    // -------------------------------------------------------------------------

    [Fact]
    public void CoordinateGuard_Always_ThrowsPolicyViolationException()
    {
        var element = MakeElement();

        Action act = () => JavaBridgeElementFinder.CoordinateGuard(element);

        act.Should().Throw<PolicyViolationException>()
            .Which.PolicyCode.Should().Be("JAB anchor insufficient for coordinate input");
    }

    [Fact]
    public void CoordinateGuard_ExceptionMessage_MentionsUiaAnchorRequired()
    {
        var element = MakeElement("SubmitButton");

        Action act = () => JavaBridgeElementFinder.CoordinateGuard(element);

        act.Should().Throw<PolicyViolationException>()
            .Which.Message.Should().Contain("UIA anchor");
    }

    // -------------------------------------------------------------------------
    // AC: MockJavaBridgeProvider returns element → FindAsync returns correct JavaElement
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FindAsync_WhenMockReturnsElement_ReturnsMatchingElement()
    {
        var expected = MakeElement("Username", "text");
        var provider = new MockJavaBridgeProvider
        {
            AvailableResult = true,
            FindResult = expected
        };
        var finder = new JavaBridgeElementFinder(provider);

        var result = await finder.FindAsync(MakeSelector("Username", "text"), new IntPtr(1));

        result.Should().NotBeNull();
        result.Name.Should().Be("Username");
        result.Role.Should().Be("text");
    }

    [Fact]
    public async Task FindAsync_WhenMockReturnsElement_WindowHandlePreserved()
    {
        var windowHandle = new IntPtr(999);
        var expected = new JavaElement
        {
            WindowHandle = windowHandle,
            Name = "OK",
            Role = "push button",
            X = 10, Y = 20, Width = 60, Height = 25
        };
        var provider = new MockJavaBridgeProvider { AvailableResult = true, FindResult = expected };
        var finder = new JavaBridgeElementFinder(provider);

        var result = await finder.FindAsync(MakeSelector("OK"), windowHandle);

        result.WindowHandle.Should().Be(windowHandle);
    }

    [Fact]
    public async Task FindAsync_WhenMockReturnsNull_ThrowsDesktopActionError()
    {
        var provider = new MockJavaBridgeProvider { AvailableResult = true, FindResult = null };
        var finder = new JavaBridgeElementFinder(provider);

        Func<Task> act = () => finder.FindAsync(MakeSelector("Ghost"), IntPtr.Zero);

        var ex = await act.Should().ThrowAsync<DesktopActionError>();
        ex.Which.ErrorCode.Should().Be("TARGET_NOT_FOUND");
    }

    // -------------------------------------------------------------------------
    // AC: Cancellation is respected
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FindAsync_WhenCancelledBeforeStart_ThrowsOperationCanceledException()
    {
        var provider = new MockJavaBridgeProvider { AvailableResult = true, FindResult = MakeElement() };
        var finder = new JavaBridgeElementFinder(provider);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => finder.FindAsync(MakeSelector(), IntPtr.Zero, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // -------------------------------------------------------------------------
    // AC: MockJavaBridgeProvider state recording
    // -------------------------------------------------------------------------

    [Fact]
    public void MockProvider_InvokeClick_RecordsElement()
    {
        var provider = new MockJavaBridgeProvider();
        var element = MakeElement("Submit");

        provider.InvokeClick(element);

        provider.LastClickedElement.Should().Be(element);
    }

    [Fact]
    public void MockProvider_InvokeTypeText_RecordsElementAndText()
    {
        var provider = new MockJavaBridgeProvider();
        var element = MakeElement("Username", "text");

        provider.InvokeTypeText(element, "hello@example.com");

        provider.LastTypedText.Should().NotBeNull();
        provider.LastTypedText!.Value.Element.Should().Be(element);
        provider.LastTypedText!.Value.Text.Should().Be("hello@example.com");
    }

    // -------------------------------------------------------------------------
    // AC: PolicyViolationException carries machine-readable PolicyCode
    // -------------------------------------------------------------------------

    [Fact]
    public void PolicyViolationException_CarriesPolicyCode()
    {
        var ex = new PolicyViolationException("JAB anchor insufficient for coordinate input",
            "A UIA anchor is required.");

        ex.PolicyCode.Should().Be("JAB anchor insufficient for coordinate input");
        ex.Message.Should().Contain("A UIA anchor is required.");
    }

    // -------------------------------------------------------------------------
    // AC: JavaBridgeSelector record equality (structural)
    // -------------------------------------------------------------------------

    [Fact]
    public void JavaBridgeSelector_RecordEquality_WorksCorrectly()
    {
        var a = new JavaBridgeSelector { AccessibleName = "OK", Role = "push button" };
        var b = new JavaBridgeSelector { AccessibleName = "OK", Role = "push button" };
        var c = new JavaBridgeSelector { AccessibleName = "Cancel", Role = "push button" };

        a.Should().Be(b);
        a.Should().NotBe(c);
    }

    // -------------------------------------------------------------------------
    // AC: JavaElement record equality (structural)
    // -------------------------------------------------------------------------

    [Fact]
    public void JavaElement_RecordEquality_WorksCorrectly()
    {
        var a = MakeElement("OK", "push button");
        var b = MakeElement("OK", "push button");
        var c = MakeElement("Cancel", "push button");

        a.Should().Be(b);
        a.Should().NotBe(c);
    }
}
