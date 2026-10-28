namespace Tracto.Designer.Core.Signing;

using Tracto.Designer.Core.Scripting;

public sealed class NullScriptSigner : IScriptSigner
{
    public string SignerId => "null";

    public ScriptSignature Sign(Script script) =>
        new("null", "none", DateTimeOffset.UtcNow, "0000000000000000000000000000000000000000000000000000000000000000");

    public bool Verify(Script script, ScriptSignature signature) => signature.SignerId == "null";
}
