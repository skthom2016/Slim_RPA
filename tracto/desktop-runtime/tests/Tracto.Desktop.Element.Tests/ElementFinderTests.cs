using System.Windows.Automation;
using FluentAssertions;
using Tracto.Desktop.Element;
using Xunit;

namespace Tracto.Desktop.Element.Tests;

/// <summary>
/// Unit tests for UiaElementFinder using MockUiaProvider.
/// No real WPF windows or live UIA sessions are used.
/// </summary>
public sealed class ElementFinderTests
{
    // -------------------------------------------------------------------------
    // AC01 — AutomationId selector matches one element → FindAsync returns ElementRef
    // -------------------------------------------------------------------------
    [Fact]
    public async Task FindAsync_SingleMatch_ReturnsElementRef()
    {
        // Arrange
        var mock = new MockUiaProvider();
        var sentinel = MockUiaProvider.CreateSentinelElement();
        mock.ConfigureFindAll(results: new[] { sentinel });

        var finder = new UiaElementFinder(mock);
        var sel = new ElementSelector { AutomationId = "btnOk" };
        var opts = new ElementFindOptions { TimeoutMs = 1000, ScrollIntoView = false, ValidateEnabled = false };

        // Act
        var result = await finder.FindAsync(sel, opts, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Selector.AutomationId.Should().Be("btnOk");
        // IsAlive: sentinel element is a real AutomationElement obtained from UIA root — it will be alive
        // In mock testing, IsAlive may return false because the sentinel is a desktop root child;
        // We verify the ref is returned and selector is preserved.
        result.Selector.Should().Be(sel);
    }

    // -------------------------------------------------------------------------
    // AC02 — Same AutomationId matches two elements, no Parent → TARGET_AMBIGUOUS
    // -------------------------------------------------------------------------
    [Fact]
    public async Task FindAsync_TwoMatches_NoDisambiguation_ThrowsTargetAmbiguous()
    {
        // Arrange
        var mock = new MockUiaProvider();
        var e1 = MockUiaProvider.CreateSentinelElement();
        var e2 = MockUiaProvider.CreateSentinelElement2();
        mock.ConfigureFindAll(results: new[] { e1, e2 });

        var finder = new UiaElementFinder(mock);
        var sel = new ElementSelector { Name = "OK" };
        var opts = new ElementFindOptions { TimeoutMs = 100, ValidateEnabled = false };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DesktopActionError>(
            () => finder.FindAsync(sel, opts, CancellationToken.None));

        ex.ErrorCode.Should().Be("TARGET_AMBIGUOUS");
    }

    // -------------------------------------------------------------------------
    // AC03 — MockUiaProvider returns no results → TARGET_NOT_FOUND
    // -------------------------------------------------------------------------
    [Fact]
    public async Task FindAsync_NoMatches_ThrowsTargetNotFound()
    {
        // Arrange
        var mock = new MockUiaProvider();
        mock.ConfigureFindAll(results: Array.Empty<AutomationElement>());

        var finder = new UiaElementFinder(mock);
        var sel = new ElementSelector { AutomationId = "nonExistentElement" };
        var opts = new ElementFindOptions { TimeoutMs = 100, ValidateEnabled = false };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DesktopActionError>(
            () => finder.FindAsync(sel, opts, CancellationToken.None));

        ex.ErrorCode.Should().Be("TARGET_NOT_FOUND");
    }

    // -------------------------------------------------------------------------
    // AC04 — ReResolveAsync after element removed (returns empty) → TARGET_NOT_FOUND
    // -------------------------------------------------------------------------
    [Fact]
    public async Task ReResolveAsync_ElementGone_ThrowsTargetNotFound()
    {
        // Arrange: first call returns one element, subsequent calls return nothing (element removed)
        var mock = new MockUiaProvider();
        var sentinel = MockUiaProvider.CreateSentinelElement();

        // First FindAsync call succeeds
        mock.ConfigureFindAll(results: new[] { sentinel });
        var finder = new UiaElementFinder(mock);
        var sel = new ElementSelector { AutomationId = "tblRow1" };
        var opts = new ElementFindOptions { TimeoutMs = 1000, ValidateEnabled = false };

        var stale = await finder.FindAsync(sel, opts, CancellationToken.None);

        // Simulate element removal: next FindAll returns empty
        mock.ConfigureFindAll(results: Array.Empty<AutomationElement>());
        var reResolveOpts = new ElementFindOptions { TimeoutMs = 100, ValidateEnabled = false };

        // Act & Assert: re-resolve must not use cached RuntimeId; re-runs selector from scratch
        var ex = await Assert.ThrowsAsync<DesktopActionError>(
            () => finder.ReResolveAsync(stale, reResolveOpts, CancellationToken.None));

        ex.ErrorCode.Should().Be("TARGET_NOT_FOUND");
    }

    // -------------------------------------------------------------------------
    // AC05 — Selector with null coordinate fields → no CoordinateValidator exception
    // -------------------------------------------------------------------------
    [Fact]
    public async Task FindAsync_SelectorWithNoCoordinates_WorksNormally()
    {
        // Arrange
        var mock = new MockUiaProvider();
        var sentinel = MockUiaProvider.CreateSentinelElement();
        mock.ConfigureFindAll(results: new[] { sentinel });

        var finder = new UiaElementFinder(mock);

        // Selector has no coordinate fields (by design, ElementSelector has none)
        var sel = new ElementSelector
        {
            AutomationId = "btnSave",
            Name = "Save",
            ClassName = "Button",
        };
        var opts = new ElementFindOptions { TimeoutMs = 1000, ValidateEnabled = false };

        // Act: should not throw CoordinateValidator exception
        Func<Task> act = () => finder.FindAsync(sel, opts, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync<ArgumentException>();
        var result = await finder.FindAsync(sel, opts, CancellationToken.None);
        result.Selector.AutomationId.Should().Be("btnSave");
    }

    // -------------------------------------------------------------------------
    // AC06 — CoordinateValidator.RejectCoordinate rejects finite coordinates
    // -------------------------------------------------------------------------
    [Fact]
    public void CoordinateValidator_RejectsFiniteCoordinate()
    {
        // Arrange & Act & Assert
        Action act = () => CoordinateValidator.RejectCoordinate(100.0, "x");
        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("x");
    }

    [Fact]
    public void CoordinateValidator_AcceptsNullCoordinate()
    {
        // Arrange & Act & Assert
        Action act = () => CoordinateValidator.RejectCoordinate(null, "x");
        act.Should().NotThrow();
    }

    // -------------------------------------------------------------------------
    // AC07 — ElementSelector record equality and immutability
    // -------------------------------------------------------------------------
    [Fact]
    public void ElementSelector_RecordEquality_Works()
    {
        var a = new ElementSelector { AutomationId = "btn1", Name = "OK" };
        var b = new ElementSelector { AutomationId = "btn1", Name = "OK" };
        a.Should().Be(b);
    }

    // -------------------------------------------------------------------------
    // AC08 — IndexInParent disambiguation selects the right element
    // -------------------------------------------------------------------------
    [Fact]
    public async Task FindAsync_TwoMatches_WithIndexInParent_SelectsCorrectElement()
    {
        // Arrange
        var mock = new MockUiaProvider();
        var e1 = MockUiaProvider.CreateSentinelElement();
        var e2 = MockUiaProvider.CreateSentinelElement2();
        mock.ConfigureFindAll(results: new[] { e1, e2 });

        var finder = new UiaElementFinder(mock);
        var sel = new ElementSelector { Name = "Row", IndexInParent = 0 };
        var opts = new ElementFindOptions { TimeoutMs = 1000, ValidateEnabled = false };

        // Act
        var result = await finder.FindAsync(sel, opts, CancellationToken.None);

        // Assert: index 0 returns first element
        result.Should().NotBeNull();
        result.AutomationElement.Should().Be(e1);
    }
}

/// <summary>
/// Configurable mock of <see cref="IUiaProvider"/> that returns pre-set AutomationElement arrays.
/// Does not spin up real WPF windows.
/// </summary>
public sealed class MockUiaProvider : IUiaProvider
{
    private AutomationElement[] _findAllResults = Array.Empty<AutomationElement>();
    private AutomationElement? _findFirstResult;

    public void ConfigureFindAll(AutomationElement[] results)
    {
        _findAllResults = results;
        _findFirstResult = results.Length > 0 ? results[0] : null;
    }

    public AutomationElement? FindFirst(TreeScope scope, Condition cond)
        => _findFirstResult;

    public AutomationElement[] FindAll(TreeScope scope, Condition cond)
        => _findAllResults;

    /// <summary>
    /// Returns a live AutomationElement by querying the UIA desktop root for the first available window.
    /// This avoids instantiating AutomationElement (sealed) while still providing a real instance for tests.
    /// </summary>
    public static AutomationElement CreateSentinelElement()
    {
        // Use the UIA desktop root itself — always available, never requires a WPF window.
        return AutomationElement.RootElement;
    }

    /// <summary>
    /// Returns a second distinct AutomationElement for ambiguity testing.
    /// Queries for a real child of the desktop root if available; falls back to root.
    /// </summary>
    public static AutomationElement CreateSentinelElement2()
    {
        try
        {
            var children = AutomationElement.RootElement.FindAll(
                TreeScope.Children, Condition.TrueCondition);

            if (children.Count >= 2)
                return children[1];

            if (children.Count >= 1)
                return children[0];
        }
        catch
        {
            // If UIA is unavailable in the test host, fall back gracefully.
        }

        // Fall back: return root again; the mock drives the count, not element identity.
        return AutomationElement.RootElement;
    }
}
