namespace Tracto.Designer.Unit.Tests.Output;

using FluentAssertions;
using Moq;
using Tracto.Designer.App.ViewModels;
using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Core.Signing;

/// <summary>
/// A signature belongs to the script it was made from. Holding on to it across New Script left
/// Sign disabled on the new script and let a publish carry the previous script's digest.
/// </summary>
public class SignatureLifetimeTests
{
    private static (SigningViewModel Signing, ScriptEditorViewModel Editor) Build()
    {
        var signer = new Mock<IScriptSigner>();
        signer.SetupGet(s => s.SignerId).Returns("author");
        signer.Setup(s => s.Sign(It.IsAny<Script>()))
              .Returns(new ScriptSignature("author", "RS256", DateTimeOffset.UnixEpoch, "digest-of-first"));

        var editor = new ScriptEditorViewModel();
        editor.LoadScript(new Script { Id = "first", Name = "First", Version = "1.0.0", TargetRuntime = "desktop" });
        return (new SigningViewModel(signer.Object, editor), editor);
    }

    [Fact]
    public void Starting_A_New_Script_Drops_The_Previous_Signature()
    {
        var (signing, editor) = Build();
        signing.SignCommand.Execute(null);
        signing.IsSigned.Should().BeTrue();

        editor.NewScript();

        signing.IsSigned.Should().BeFalse("the new script has not been signed");
        signing.CurrentSignature.Should().BeNull();
        signing.SignCommand.CanExecute(null).Should().BeTrue("the new script can be signed");
        signing.VerifyCommand.CanExecute(null).Should().BeFalse("there is no signature to verify");
    }

    [Fact]
    public void Opening_Another_Script_Drops_The_Previous_Signature()
    {
        var (signing, editor) = Build();
        signing.SignCommand.Execute(null);

        editor.LoadScript(new Script { Id = "second", Name = "Second", Version = "1.0.0", TargetRuntime = "browser" });

        signing.IsSigned.Should().BeFalse("an imported script carries no signature of ours");
        signing.IsVerified.Should().BeNull();
    }
}
