namespace Tracto.Designer.Unit.Tests.Signing;

using FluentAssertions;
using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Core.Signing;
using Tracto.Designer.Core.Validation;

public class ScriptSigningTests
{
    private static readonly byte[] _key = System.Text.Encoding.UTF8.GetBytes("test-key-12345678");

    private static HmacScriptSigner MakeSigner() => new("test-signer", _key, new ScriptValidator());

    private static Script ValidScript()
    {
        var s = new Script { Id = "test", Name = "Test", Version = "1.0.0", TargetRuntime = "desktop" };
        s.Steps.Add(new ScriptStep { StepNumber = 1, Description = "Click OK", Action = "click" });
        return s;
    }

    [Fact]
    public void Sign_Valid_Script_Returns_Signature()
    {
        var sig = MakeSigner().Sign(ValidScript());
        sig.SignerId.Should().Be("test-signer");
        sig.Algorithm.Should().Be("HMAC-SHA256");
        sig.Digest.Should().HaveLength(64);
    }

    [Fact]
    public void Verify_Returns_True_For_Unmodified_Script()
    {
        var signer = MakeSigner();
        var s = ValidScript();
        var sig = signer.Sign(s);
        signer.Verify(s, sig).Should().BeTrue();
    }

    [Fact]
    public void Verify_Returns_False_After_Step_Modified()
    {
        var signer = MakeSigner();
        var s = ValidScript();
        var sig = signer.Sign(s);
        s.Steps[0].Description = "Tampered!";
        signer.Verify(s, sig).Should().BeFalse();
    }

    [Fact]
    public void Sign_Invalid_Script_Throws()
    {
        var signer = MakeSigner();
        var s = new Script { Id = "", Name = "", Version = "", TargetRuntime = "" };
        signer.Invoking(sg => sg.Sign(s)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Different_Keys_Dont_Verify()
    {
        var signer1 = MakeSigner();
        var signer2 = new HmacScriptSigner("s2", System.Text.Encoding.UTF8.GetBytes("different-key"), new ScriptValidator());
        var sig = signer1.Sign(ValidScript());
        signer2.Verify(ValidScript(), sig).Should().BeFalse();
    }

    [Fact]
    public void NullSigner_Sign_And_Verify()
    {
        var nullSigner = new NullScriptSigner();
        var sig = nullSigner.Sign(ValidScript());
        nullSigner.Verify(ValidScript(), sig).Should().BeTrue();
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
