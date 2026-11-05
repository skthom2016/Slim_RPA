using FluentAssertions;
using Microsoft.Playwright;
using Moq;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Locators;
using TractoRuntime.Browser.Tests.Fixtures;

namespace TractoRuntime.Browser.Tests;

/// <summary>
/// Playwright matches an accessible name, a label and a placeholder as a substring by default,
/// so a recorded value also matched every longer value containing it. In Outlook, role=button
/// name="New" matched "New", "Newsletters" and "Expand to see more New options", and
/// label="Subject" matched "Add a subject" as well as the subject box. Both failed the run as
/// ambiguous. Every value is now matched whole.
/// </summary>
public sealed class WholeValueMatchingTests : BrowserTestBase
{
    private readonly LocatorResolver _sut = new();

    private static Mock<ILocator> LocatorWith(int count)
    {
        var locator = new Mock<ILocator>();
        locator.Setup(l => l.CountAsync()).ReturnsAsync(count);
        return locator;
    }

    [Fact]
    public async Task An_Accessible_Name_Is_Matched_Whole()
    {
        var exact = LocatorWith(1);
        var page = new Mock<IPage>();
        page.Setup(p => p.GetByRole(AriaRole.Button,
                It.Is<PageGetByRoleOptions?>(o => o != null && o.Name == "New" && o.Exact == true)))
            .Returns(exact.Object);

        var result = await _sut.ResolveAsync(page.Object,
            new SelectorRef { Role = "button", RoleName = "New" }, CancellationToken.None);

        result.Should().BeSameAs(exact.Object,
            "only the element named exactly New is the one the author recorded");
    }

    [Fact]
    public async Task A_Label_Is_Matched_Whole()
    {
        var exact = LocatorWith(1);
        var page = new Mock<IPage>();
        page.Setup(p => p.GetByLabel("Subject",
                It.Is<PageGetByLabelOptions?>(o => o != null && o.Exact == true)))
            .Returns(exact.Object);

        var result = await _sut.ResolveAsync(page.Object,
            new SelectorRef { Label = "Subject" }, CancellationToken.None);

        result.Should().BeSameAs(exact.Object);
    }

    [Fact]
    public async Task A_Placeholder_Is_Matched_Whole()
    {
        var exact = LocatorWith(1);
        var page = new Mock<IPage>();
        page.Setup(p => p.GetByPlaceholder("Search the web",
                It.Is<PageGetByPlaceholderOptions?>(o => o != null && o.Exact == true)))
            .Returns(exact.Object);

        var result = await _sut.ResolveAsync(page.Object,
            new SelectorRef { Placeholder = "Search the web" }, CancellationToken.None);

        result.Should().BeSameAs(exact.Object);
    }

    [Fact]
    public async Task A_Partial_Value_Is_Reported_As_Not_Found_Rather_Than_Guessed_At()
    {
        // "Sen" no longer quietly resolves to "Send": naming part of a value is a mistake, and
        // a clear not-found beats clicking something the author never meant.
        var page = new Mock<IPage>();
        page.Setup(p => p.GetByRole(AriaRole.Button, It.IsAny<PageGetByRoleOptions?>()))
            .Returns(LocatorWith(0).Object);

        var act = () => _sut.ResolveAsync(page.Object,
            new SelectorRef { Role = "button", RoleName = "Sen" }, CancellationToken.None);

        await act.Should().ThrowAsync<LocatorNotFoundException>();
    }

    [Fact]
    public async Task Several_Elements_Named_Exactly_The_Same_Are_Still_Refused()
    {
        var page = new Mock<IPage>();
        page.Setup(p => p.GetByRole(AriaRole.Button, It.IsAny<PageGetByRoleOptions?>()))
            .Returns(LocatorWith(2).Object);

        var act = () => _sut.ResolveAsync(page.Object,
            new SelectorRef { Role = "button", RoleName = "Delete" }, CancellationToken.None);

        await act.Should().ThrowAsync<LocatorAmbiguityException>(
            "the runtime never silently clicks the first of several matches");
    }

    [Fact]
    public async Task A_Role_Without_A_Name_Is_Unchanged()
    {
        var byRole = LocatorWith(1);
        var page = new Mock<IPage>();
        page.Setup(p => p.GetByRole(AriaRole.Button, It.Is<PageGetByRoleOptions?>(o => o == null)))
            .Returns(byRole.Object);

        var result = await _sut.ResolveAsync(page.Object,
            new SelectorRef { Role = "button" }, CancellationToken.None);

        result.Should().BeSameAs(byRole.Object);
    }
}
