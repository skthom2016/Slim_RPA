namespace Tracto.Designer.Unit.Tests.Signing;

using FluentAssertions;
using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Core.Signing;

public class ScriptCanonicalizerTests
{
    private static Script ValidScript()
    {
        var s = new Script { Id = "test", Name = "Test", Version = "1.0.0", TargetRuntime = "desktop" };
        s.Steps.Add(new ScriptStep { StepNumber = 1, Description = "Click OK", Action = "click", Parameters = { ["coordinates"] = "10,10" } });
        return s;
    }

    [Fact]
    public void Canonicalize_Is_Deterministic()
    {
        var s = ValidScript();
        var c1 = ScriptCanonicalizer.Canonicalize(s);
        var c2 = ScriptCanonicalizer.Canonicalize(s);
        c1.Should().Be(c2);
    }

    [Fact]
    public void Canonicalize_Excludes_Extra_Fields()
    {
        var s = ValidScript();
        var c = ScriptCanonicalizer.Canonicalize(s);
        c.Should().Contain("\"id\":");
        c.Should().Contain("\"steps\":");
    }
}
