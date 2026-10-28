namespace Tracto.Designer.Unit.Tests.Templates;

using FluentAssertions;
using Tracto.Designer.Core.Templates;

public class TemplateLibraryTests
{
    private static readonly BuiltInTemplateLibrary Lib = new();

    [Fact]
    public void GetAll_Returns_At_Least_8_Templates()
    {
        Lib.GetAll().Should().HaveCountGreaterOrEqualTo(8);
    }

    [Fact]
    public void GetAll_Covers_All_Categories()
    {
        var cats = Lib.GetAll().Select(t => t.Category).Distinct();
        cats.Should().Contain(TemplateCategory.Login);
        cats.Should().Contain(TemplateCategory.Navigation);
        cats.Should().Contain(TemplateCategory.DataEntry);
        cats.Should().Contain(TemplateCategory.Verification);
        cats.Should().Contain(TemplateCategory.FileOperation);
        cats.Should().Contain(TemplateCategory.General);
    }

    [Fact]
    public void GetByCategory_Filters_Correctly()
    {
        var logins = Lib.GetByCategory(TemplateCategory.Login);
        logins.Should().NotBeEmpty();
        logins.All(t => t.Category == TemplateCategory.Login).Should().BeTrue();
    }

    [Fact]
    public void Search_Finds_By_Name()
    {
        var r = Lib.Search("login");
        r.Should().NotBeEmpty();
    }

    [Fact]
    public void Search_Finds_By_Tag()
    {
        var r = Lib.Search("download");
        r.Should().NotBeEmpty();
    }

    [Fact]
    public void Search_Empty_Returns_All()
    {
        Lib.Search("").Should().HaveCountGreaterOrEqualTo(8);
    }

    [Fact]
    public void CreateScript_Is_Deterministic()
    {
        var t = Lib.GetAll().First();
        var s1 = t.CreateScript();
        var s2 = t.CreateScript();
        s1.Id.Should().Be(s2.Id);
        s1.Steps.Count.Should().Be(s2.Steps.Count);
        // Also verify that step content (not just count) is identical across calls.
        for (int i = 0; i < s1.Steps.Count; i++)
        {
            s1.Steps[i].Action.Should().Be(s2.Steps[i].Action,
                because: $"step {i} action must be deterministic");
            s1.Steps[i].Description.Should().Be(s2.Steps[i].Description,
                because: $"step {i} description must be deterministic");
            s1.Steps[i].Parameters.Should().BeEquivalentTo(s2.Steps[i].Parameters,
                because: $"step {i} parameters must be deterministic");
        }
    }

    [Fact]
    public void LoginTemplate_Has_Password_Step()
    {
        var t = Lib.GetByCategory(TemplateCategory.Login).First();
        var s = t.CreateScript();
        s.Steps.Should().Contain(step => step.Action == "type" && step.Parameters.ContainsKey("value"));
    }

    [Fact]
    public void All_Template_Steps_Have_Action_And_Description()
    {
        foreach (var t in Lib.GetAll())
        {
            var s = t.CreateScript();
            s.Steps.All(step => !string.IsNullOrEmpty(step.Action) && !string.IsNullOrEmpty(step.Description))
                .Should().BeTrue(because: $"template '{t.Name}' has empty step fields");
        }
    }

    [Fact]
    public void Template_Ids_Are_Unique()
    {
        var ids = Lib.GetAll().Select(t => t.Id).ToList();
        ids.Should().OnlyHaveUniqueItems();
    }
}
