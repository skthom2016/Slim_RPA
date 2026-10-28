using FluentAssertions;
using Tracto.Desktop.Element;
using Xunit;

namespace Tracto.Desktop.Element.Tests;

/// <summary>
/// Unit tests for DT-07: ElementStateGuard.AssertActionable.
/// All tests are pure model tests — no real UIA or WPF windows required.
/// </summary>
public sealed class ElementStateTests
{
    // -------------------------------------------------------------------------
    // DT-07-AC01 — IsEnabled=false produces a single, predictable diagnostic.
    // Callers must NOT receive TARGET_NOT_FOUND for a located-but-disabled control.
    // -------------------------------------------------------------------------
    [Fact]
    public void AssertActionable_ElementDisabled_ThrowsElementDisabled()
    {
        var state = new ElementStateResult
        {
            IsEnabled = false,
            IsVisible = true,
            HasProvider = true,
            AccessDeniedReason = null,
        };

        var ex = Assert.Throws<DesktopActionError>(() => ElementStateGuard.AssertActionable(state));

        ex.ErrorCategory.Should().Be("ENVIRONMENT");
        ex.DetailCode.Should().Be("desktop.elementDisabled");

        // Explicit: not TARGET_NOT_FOUND (DT-07-AC01 requirement).
        ex.ErrorCategory.Should().NotBe("TARGET_NOT_FOUND");
    }

    // -------------------------------------------------------------------------
    // DT-07-AC07 — Elevated-process access-denied maps to POLICY_DENIED.
    // -------------------------------------------------------------------------
    [Fact]
    public void AssertActionable_AccessDenied_ThrowsPolicyDenied()
    {
        var state = new ElementStateResult
        {
            IsEnabled = true,
            IsVisible = true,
            HasProvider = true,
            AccessDeniedReason = "POLICY_DENIED",
        };

        var ex = Assert.Throws<DesktopActionError>(() => ElementStateGuard.AssertActionable(state));

        ex.ErrorCategory.Should().Be("POLICY_DENIED");
        ex.DetailCode.Should().Be("desktop.accessDenied");
    }

    // -------------------------------------------------------------------------
    // DT-07-AC06 — No UIA provider maps to desktop.unsupportedControl.
    // -------------------------------------------------------------------------
    [Fact]
    public void AssertActionable_NoProvider_ThrowsUnsupportedControl()
    {
        var state = new ElementStateResult
        {
            IsEnabled = true,
            IsVisible = true,
            HasProvider = false,
            AccessDeniedReason = null,
        };

        var ex = Assert.Throws<DesktopActionError>(() => ElementStateGuard.AssertActionable(state));

        ex.ErrorCategory.Should().Be("ENVIRONMENT");
        ex.DetailCode.Should().Be("desktop.unsupportedControl");
    }

    // -------------------------------------------------------------------------
    // DT-07 — Off-screen element maps to desktop.elementOffscreen.
    // -------------------------------------------------------------------------
    [Fact]
    public void AssertActionable_ElementOffscreen_ThrowsElementOffscreen()
    {
        var state = new ElementStateResult
        {
            IsEnabled = true,
            IsVisible = false,
            HasProvider = true,
            AccessDeniedReason = null,
        };

        var ex = Assert.Throws<DesktopActionError>(() => ElementStateGuard.AssertActionable(state));

        ex.ErrorCategory.Should().Be("ENVIRONMENT");
        ex.DetailCode.Should().Be("desktop.elementOffscreen");
    }

    // -------------------------------------------------------------------------
    // DT-07 — Fully actionable element does not throw.
    // -------------------------------------------------------------------------
    [Fact]
    public void AssertActionable_FullyActionable_DoesNotThrow()
    {
        var state = new ElementStateResult
        {
            IsEnabled = true,
            IsVisible = true,
            HasProvider = true,
            AccessDeniedReason = null,
        };

        Action act = () => ElementStateGuard.AssertActionable(state);
        act.Should().NotThrow();
    }

    // -------------------------------------------------------------------------
    // DT-07 — Access-denied takes priority over IsEnabled=false.
    // -------------------------------------------------------------------------
    [Fact]
    public void AssertActionable_AccessDeniedAndDisabled_PolicyDeniedWins()
    {
        var state = new ElementStateResult
        {
            IsEnabled = false,
            IsVisible = false,
            HasProvider = false,
            AccessDeniedReason = "POLICY_DENIED",
        };

        var ex = Assert.Throws<DesktopActionError>(() => ElementStateGuard.AssertActionable(state));

        // POLICY_DENIED wins — it is checked first.
        ex.ErrorCategory.Should().Be("POLICY_DENIED");
        ex.DetailCode.Should().Be("desktop.accessDenied");
    }
}
